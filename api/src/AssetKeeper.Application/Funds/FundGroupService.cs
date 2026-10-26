using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Funds;

public sealed class FundGroupService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public FundGroupService(ILedgerRepository ledgerRepository, BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<IReadOnlyList<FundGroupDto>?> GetAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);
        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);

        return Map(groups, positions);
    }

    public async Task<FundGroupOperationResult> CreateAsync(
        CreateFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return FundGroupOperationResult.NotFound("账本尚未初始化。");
        }

        string name;

        try
        {
            name = FundGroup.NormalizeName(request.Name);
        }
        catch (ArgumentException exception)
        {
            return FundGroupOperationResult.Invalid([exception.Message]);
        }

        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);

        if (groups.Any(group => string.Equals(group.Name, name, StringComparison.Ordinal)))
        {
            return FundGroupOperationResult.Conflict("已存在同名分组。");
        }

        var sortOrder = groups.Select(group => group.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        var created = FundGroup.Create(ledger.Id, name, sortOrder, _dates.UtcNow);
        await _ledgerRepository.AddFundGroupAsync(created, cancellationToken);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return FundGroupOperationResult.Conflict("分组已变化，请刷新后重试。");
        }

        return FundGroupOperationResult.Created(new FundGroupDto(
            created.Id,
            created.Name,
            created.SortOrder,
            0,
            0,
            0m));
    }

    public async Task<FundGroupOperationResult> RenameAsync(
        Guid groupId,
        RenameFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return FundGroupOperationResult.NotFound("账本尚未初始化。");
        }

        var group = await _ledgerRepository.GetFundGroupForUpdateAsync(ledger.Id, groupId, cancellationToken);

        if (group is null)
        {
            return FundGroupOperationResult.NotFound("分组不存在。");
        }

        string name;

        try
        {
            name = FundGroup.NormalizeName(request.Name);
        }
        catch (ArgumentException exception)
        {
            return FundGroupOperationResult.Invalid([exception.Message]);
        }

        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);

        if (groups.Any(other => other.Id != group.Id
            && string.Equals(other.Name, name, StringComparison.Ordinal)))
        {
            return FundGroupOperationResult.Conflict("已存在同名分组。");
        }

        group.Rename(name, _dates.UtcNow);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return FundGroupOperationResult.Conflict("分组已变化，请刷新后重试。");
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var mapped = Map(groups.Select(item => item.Id == group.Id ? group : item).ToList(), positions)
            .Single(item => item.Id == group.Id);

        return FundGroupOperationResult.Success(mapped);
    }

    public async Task<FundGroupOperationResult> DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return FundGroupOperationResult.NotFound("账本尚未初始化。");
        }

        var group = await _ledgerRepository.GetFundGroupForUpdateAsync(ledger.Id, groupId, cancellationToken);

        if (group is null)
        {
            return FundGroupOperationResult.NotFound("分组不存在。");
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var now = _dates.UtcNow;

        foreach (var position in positions.Where(position => position.FundGroupId == group.Id))
        {
            var tracked = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, position.Id, cancellationToken);
            tracked?.AssignGroup(null, now);
        }

        _ledgerRepository.RemoveFundGroup(group);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return FundGroupOperationResult.Conflict("分组或持仓已变化，请刷新后重试。");
        }

        return FundGroupOperationResult.Deleted();
    }

    public async Task<FundGroupOperationResult> AssignAsync(
        Guid positionId,
        AssignFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return FundGroupOperationResult.NotFound("账本尚未初始化。");
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, positionId, cancellationToken);

        if (position is null)
        {
            return FundGroupOperationResult.NotFound("基金持仓不存在。");
        }

        if (request.GroupId is not null
            && await _ledgerRepository.GetFundGroupForUpdateAsync(ledger.Id, request.GroupId.Value, cancellationToken) is null)
        {
            return FundGroupOperationResult.NotFound("分组不存在。");
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var now = _dates.UtcNow;
        var changed = new List<Guid>();

        var familyKeys = FundIdentity.FamilyKeys(positions);

        foreach (var item in positions.Where(item => familyKeys[item.Id] == familyKeys[position.Id]))
        {
            var tracked = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, item.Id, cancellationToken);

            if (tracked is null || tracked.FundGroupId == request.GroupId)
            {
                continue;
            }

            tracked.AssignGroup(request.GroupId, now);
            changed.Add(tracked.Id);
        }

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return FundGroupOperationResult.Conflict("持仓已变化，请刷新后重试。");
        }

        return FundGroupOperationResult.Success(new FundGroupAssignmentDto(request.GroupId, changed));
    }

    private static IReadOnlyList<FundGroupDto> Map(
        IReadOnlyList<FundGroup> groups,
        IReadOnlyList<FundPosition> positions)
    {
        var familyKeys = FundIdentity.FamilyKeys(positions);
        var effectiveGroupIds = FundIdentity.EffectiveGroupIds(positions);

        return groups
            .Select(group =>
            {
                var members = positions.Where(position => effectiveGroupIds[position.Id] == group.Id).ToList();
                return new FundGroupDto(
                    group.Id,
                    group.Name,
                    group.SortOrder,
                    members.Select(position => familyKeys[position.Id]).Distinct(StringComparer.Ordinal).Count(),
                    members.Count,
                    decimal.Round(members.Sum(position => position.Value), 2));
            })
            .ToList();
    }
}
