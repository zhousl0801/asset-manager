using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed class LedgerEntryService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public LedgerEntryService(ILedgerRepository ledgerRepository, BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<EntryPeriodDto?> GetPeriodAsync(
        EntryPeriod period,
        DateOnly? anchor,
        LedgerEntryKind? kind,
        string? category,
        string? search,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 100)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var anchorDate = anchor ?? _businessDateProvider.Today;
        var (from, to) = EntryPeriodRange.For(period, anchorDate);
        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var totals = await _ledgerRepository.GetEntryTotalsAsync(ledger.Id, from, to, cancellationToken);
        var entries = await _ledgerRepository.GetEntryPageAsync(ledger.Id, from, to, kind, category, search, page, pageSize, cancellationToken);

        return new EntryPeriodDto(period, from, to, totals.Income, totals.Expense, totals.Income - totals.Expense,
            entries.Items.Select(Map).ToList(), page, pageSize, entries.TotalCount);
    }

    public async Task<LedgerEntryDto?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await _ledgerRepository.GetEntryAsync(entryId, cancellationToken);

        return entry is null ? null : Map(entry);
    }

    public Task<EntryOperationResult> CreateAsync(CreateLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "entry.create", request.RequestId, request,
            () => CreateCoreAsync(request, cancellationToken),
            result => result.Status is EntryOperationStatus.Success or EntryOperationStatus.Created,
            result => result with { Status = EntryOperationStatus.Success },
            EntryOperationResult.Conflict,
            message => EntryOperationResult.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<EntryOperationResult> CreateCoreAsync(
        CreateLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return EntryOperationResult.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        var businessToday = _businessDateProvider.Today;
        var errors = LedgerEntry.Validate(
            request.Kind,
            request.Category,
            request.Amount,
            request.BusinessDate,
            ledger.StartDate,
            businessToday,
            request.Note);

        if (request.RequestId == Guid.Empty)
        {
            errors.Add("请求标识无效。");
        }

        if (errors.Count > 0)
        {
            return EntryOperationResult.Invalid(errors);
        }

        var categoryError = await ValidateCategoryAsync(
            ledger.Id,
            request.Kind,
            request.Category,
            allowInactive: false,
            cancellationToken);

        if (categoryError is not null)
        {
            return EntryOperationResult.Invalid([categoryError]);
        }

        var now = _businessDateProvider.UtcNow;
        var entry = LedgerEntry.Create(
            ledger.Id,
            request.Kind,
            request.Category,
            request.Amount,
            request.BusinessDate,
            ledger.StartDate,
            businessToday,
            request.Note,
            LedgerEntrySource.Manual,
            request.RequestId,
            now);

        entry.SetHistoryMetadata(ledger.AllocateFactSequence(), request.RelatedCashAdjustmentId);
        await _ledgerRepository.AddEntryAsync(entry, cancellationToken);
        await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).RebuildAsync(ledger.Id, entry.Id, cancellationToken);


        return EntryOperationResult.Created(
            new EntryMutationResultDto(Map(entry), ledger.CashAccount.Balance));
    }

    public Task<EntryOperationResult> UpdateAsync(Guid entryId, UpdateLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "entry.update", request.RequestId, new { EntryId = entryId, Request = request },
            () => UpdateCoreAsync(entryId, request, cancellationToken),
            result => result.Status is EntryOperationStatus.Success or EntryOperationStatus.Created,
            result => result with { Status = EntryOperationStatus.Success },
            EntryOperationResult.Conflict,
            message => EntryOperationResult.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<EntryOperationResult> UpdateCoreAsync(
        Guid entryId,
        UpdateLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return EntryOperationResult.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        var entry = await _ledgerRepository.GetEntryAsync(entryId, cancellationToken);

        if (entry is null || entry.LedgerId != ledger.Id)
        {
            return EntryOperationResult.NotFound("记录不存在。");
        }

        if (entry.Status != LedgerEntryStatus.Active)
        {
            return EntryOperationResult.Conflict("该记录已撤销，不能修改。");
        }

        var businessToday = _businessDateProvider.Today;
        var errors = LedgerEntry.Validate(
            request.Kind,
            request.Category,
            request.Amount,
            request.BusinessDate,
            ledger.StartDate,
            businessToday,
            request.Note);

        if (errors.Count > 0)
        {
            return EntryOperationResult.Invalid(errors);
        }

        var keepsCategory = entry.Kind == request.Kind
            && string.Equals(entry.Category, request.Category.Trim(), StringComparison.Ordinal);
        var categoryError = await ValidateCategoryAsync(
            ledger.Id,
            request.Kind,
            request.Category,
            allowInactive: keepsCategory,
            cancellationToken);

        if (categoryError is not null)
        {
            return EntryOperationResult.Invalid([categoryError]);
        }

        var now = _businessDateProvider.UtcNow;

        entry.Update(
            request.Kind,
            request.Category,
            request.Amount,
            request.BusinessDate,
            ledger.StartDate,
            businessToday,
            request.Note,
            now);
        entry.SetHistoryMetadata(entry.BusinessSequence, request.RelatedCashAdjustmentId);
        await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).RebuildAsync(ledger.Id, entry.Id, cancellationToken);

        return EntryOperationResult.Success(
            new EntryMutationResultDto(Map(entry), ledger.CashAccount.Balance));
    }

    public Task<EntryOperationResult> ReverseAsync(Guid entryId, ReverseLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "entry.reverse", request.RequestId, new { EntryId = entryId, Request = request },
            () => ReverseCoreAsync(entryId, request, cancellationToken),
            result => result.Status is EntryOperationStatus.Success or EntryOperationStatus.Created,
            result => result with { Status = EntryOperationStatus.Success },
            EntryOperationResult.Conflict,
            message => EntryOperationResult.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<EntryOperationResult> ReverseCoreAsync(
        Guid entryId,
        ReverseLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return EntryOperationResult.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        var entry = await _ledgerRepository.GetEntryAsync(entryId, cancellationToken);

        if (entry is null || entry.LedgerId != ledger.Id)
        {
            return EntryOperationResult.NotFound("记录不存在。");
        }

        if (entry.Status != LedgerEntryStatus.Active)
        {
            return EntryOperationResult.Conflict("该记录已撤销。");
        }

        if (request.Reason?.Trim().Length > 200)
        {
            return EntryOperationResult.Invalid(new[] { "撤销原因不能超过 200 个字符。" });
        }

        var now = _businessDateProvider.UtcNow;
        var businessToday = _businessDateProvider.Today;

        entry.Reverse(request.Reason, now);
        await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).RebuildAsync(ledger.Id, entry.Id, cancellationToken);


        return EntryOperationResult.Success(
            new EntryMutationResultDto(Map(entry), ledger.CashAccount.Balance));
    }

    private async Task<string?> ValidateCategoryAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string name,
        bool allowInactive,
        CancellationToken cancellationToken)
    {
        EntryCategory? category;

        try
        {
            category = await _ledgerRepository.GetCategoryAsync(ledgerId, kind, name, cancellationToken);
        }
        catch (ArgumentException)
        {
            return "请选择有效分类。";
        }

        if (category is null)
        {
            return "请选择有效分类。";
        }

        return !allowInactive && !category.IsActive ? "该分类已停用，请重新选择。" : null;
    }

    private static LedgerEntryDto Map(LedgerEntry entry)
    {
        return new LedgerEntryDto(
            entry.Id,
            entry.Kind,
            entry.Category,
            entry.Amount,
            entry.BusinessDate,
            entry.Note,
            entry.Source,
            entry.Status,
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.ReversedAt,
            entry.ReversalReason,
            entry.RelatedCashAdjustmentId);
    }
}
