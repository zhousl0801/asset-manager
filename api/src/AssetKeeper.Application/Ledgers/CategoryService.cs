using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed class CategoryService
{
    private const string SalaryCategoryKey = "salary";

    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public CategoryService(ILedgerRepository ledgerRepository, BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<IReadOnlyList<EntryCategoryDto>?> GetAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var categories = await _ledgerRepository.GetCategoriesAsync(ledger.Id, cancellationToken);

        return categories.Select(Map).ToList();
    }

    public async Task<CategoryOperationResult> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return CategoryOperationResult.NotFound("账本尚未初始化。");
        }

        string name;

        try
        {
            name = EntryCategory.NormalizeName(request.Name);
        }
        catch (ArgumentException exception)
        {
            return CategoryOperationResult.Invalid([exception.Message]);
        }

        var categories = await _ledgerRepository.GetCategoriesAsync(ledger.Id, cancellationToken);

        if (categories.Any(category => category.Kind == request.Kind
            && string.Equals(category.Name, name, StringComparison.Ordinal)))
        {
            return CategoryOperationResult.Conflict("同类型下已存在同名分类。");
        }

        var sortOrder = categories
            .Where(category => category.Kind == request.Kind)
            .Select(category => category.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        var created = EntryCategory.Create(ledger.Id, request.Kind, name, sortOrder, null, _dates.UtcNow);
        await _ledgerRepository.AddCategoryAsync(created, cancellationToken);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return CategoryOperationResult.Conflict("分类已变化，请刷新后重试。");
        }

        return CategoryOperationResult.Created(Map(created));
    }

    public async Task<CategoryOperationResult> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return CategoryOperationResult.NotFound("账本尚未初始化。");
        }

        var category = await _ledgerRepository.GetCategoryForUpdateAsync(ledger.Id, categoryId, cancellationToken);

        if (category is null)
        {
            return CategoryOperationResult.NotFound("分类不存在。");
        }

        string name;

        try
        {
            name = EntryCategory.NormalizeName(request.Name);
        }
        catch (ArgumentException exception)
        {
            return CategoryOperationResult.Invalid([exception.Message]);
        }

        if (!request.IsActive && category.SystemKey == SalaryCategoryKey)
        {
            return CategoryOperationResult.Invalid(["工资分类由自动记账使用，不能停用。"]);
        }

        var categories = await _ledgerRepository.GetCategoriesAsync(ledger.Id, cancellationToken);

        if (categories.Any(other => other.Id != category.Id
            && other.Kind == category.Kind
            && string.Equals(other.Name, name, StringComparison.Ordinal)))
        {
            return CategoryOperationResult.Conflict("同类型下已存在同名分类。");
        }

        var previousName = category.Name;
        var now = _dates.UtcNow;
        category.SetActive(request.IsActive, now);

        if (!string.Equals(previousName, name, StringComparison.Ordinal))
        {
            category.Rename(name, now);
            await _ledgerRepository.RenameEntryCategoryAsync(
                ledger.Id,
                category.Kind,
                previousName,
                name,
                cancellationToken);
        }

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return CategoryOperationResult.Conflict("分类已变化，请刷新后重试。");
        }

        return CategoryOperationResult.Success(Map(category));
    }

    public async Task<CategoryOperationResult> ReorderAsync(
        ReorderCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return CategoryOperationResult.NotFound("账本尚未初始化。");
        }

        var categories = (await _ledgerRepository.GetCategoriesAsync(ledger.Id, cancellationToken))
            .Where(category => category.Kind == request.Kind)
            .ToList();

        if (request.Ids.Count != categories.Count
            || request.Ids.Distinct().Count() != request.Ids.Count
            || request.Ids.Any(id => categories.All(category => category.Id != id)))
        {
            return CategoryOperationResult.Invalid(["排序必须包含该类型的全部分类且不能重复。"]);
        }

        var now = _dates.UtcNow;
        var byId = categories.ToDictionary(category => category.Id);

        for (var index = 0; index < request.Ids.Count; index++)
        {
            var category = byId[request.Ids[index]];
            var tracked = await _ledgerRepository.GetCategoryForUpdateAsync(ledger.Id, category.Id, cancellationToken);

            tracked?.SetSortOrder(index, now);
        }

        await _ledgerRepository.SaveChangesAsync(cancellationToken);

        return CategoryOperationResult.Success();
    }

    private static EntryCategoryDto Map(EntryCategory category)
    {
        return new EntryCategoryDto(
            category.Id,
            category.Kind,
            category.Name,
            category.SortOrder,
            category.IsActive,
            category.SystemKey);
    }
}
