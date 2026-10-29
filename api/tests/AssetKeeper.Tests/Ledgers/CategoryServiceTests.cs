using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Ledgers;

public sealed class CategoryServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initialize_SeedsDefaultCategories()
    {
        var context = await CreateAsync();

        var categories = await context.Categories.GetAsync(CancellationToken.None);

        Assert.NotNull(categories);
        Assert.Equal(18, categories.Count);
        Assert.Equal(15, categories.Count(category => category.Kind == LedgerEntryKind.Expense));
        Assert.Equal(3, categories.Count(category => category.Kind == LedgerEntryKind.Income));
        Assert.Contains(categories, category => category.Kind == LedgerEntryKind.Income
            && category.Name == "工资"
            && category.SystemKey == "salary");
    }

    [Fact]
    public async Task Create_AppendsCategoryAndRejectsDuplicate()
    {
        var context = await CreateAsync();

        var created = await context.Categories.CreateAsync(
            new CreateCategoryRequest(LedgerEntryKind.Expense, "咖啡"),
            CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Created, created.Status);
        Assert.NotNull(created.Category);
        Assert.Equal("咖啡", created.Category.Name);
        Assert.Equal(15, created.Category.SortOrder);

        var duplicate = await context.Categories.CreateAsync(
            new CreateCategoryRequest(LedgerEntryKind.Expense, "餐饮"),
            CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task Rename_UpdatesExistingEntriesWithoutChangingKind()
    {
        var context = await CreateAsync();
        var entry = await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 35.60m, Today, "午餐", Guid.NewGuid()),
            CancellationToken.None);
        var category = (await context.Categories.GetAsync(CancellationToken.None))!
            .Single(item => item.Kind == LedgerEntryKind.Expense && item.Name == "餐饮");

        var renamed = await context.Categories.UpdateAsync(
            category.Id,
            new UpdateCategoryRequest("外食", true),
            CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Success, renamed.Status);
        Assert.Equal("外食", renamed.Category!.Name);
        Assert.Equal(LedgerEntryKind.Expense, renamed.Category.Kind);
        var updated = Assert.Single(context.Repository.Entries);
        Assert.Equal(entry.Result!.Entry.Id, updated.Id);
        Assert.Equal("外食", updated.Category);
        Assert.Equal(LedgerEntryKind.Expense, updated.Kind);
    }

    [Fact]
    public async Task DisabledCategory_RejectsNewEntriesButKeepsExistingOnes()
    {
        var context = await CreateAsync();
        var entry = await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 35.60m, Today, "午餐", Guid.NewGuid()),
            CancellationToken.None);
        var category = (await context.Categories.GetAsync(CancellationToken.None))!
            .Single(item => item.Kind == LedgerEntryKind.Expense && item.Name == "餐饮");

        await context.Categories.UpdateAsync(
            category.Id,
            new UpdateCategoryRequest("餐饮", false),
            CancellationToken.None);

        var rejected = await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 10m, Today, null, Guid.NewGuid()),
            CancellationToken.None);
        var updated = await context.Entries.UpdateAsync(
            entry.Result!.Entry.Id,
            new UpdateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 50m, Today, "晚餐", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Invalid, rejected.Status);
        Assert.Contains(rejected.Errors, error => error.Contains("已停用"));
        Assert.Equal(EntryOperationStatus.Success, updated.Status);
        Assert.Single(context.Repository.Entries);
    }

    [Fact]
    public async Task SalaryCategory_CannotBeDisabled()
    {
        var context = await CreateAsync();
        var salary = (await context.Categories.GetAsync(CancellationToken.None))!
            .Single(item => item.SystemKey == "salary");

        var result = await context.Categories.UpdateAsync(
            salary.Id,
            new UpdateCategoryRequest(salary.Name, false),
            CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("不能停用"));
    }

    [Fact]
    public async Task Delete_RemovesCategoryAndKeepsHistoricalEntriesEditable()
    {
        var context = await CreateAsync();
        var entry = await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 35.60m, Today, "午餐", Guid.NewGuid()),
            CancellationToken.None);
        var category = (await context.Categories.GetAsync(CancellationToken.None))!
            .Single(item => item.Kind == LedgerEntryKind.Expense && item.Name == "餐饮");

        var deleted = await context.Categories.DeleteAsync(category.Id, CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Success, deleted.Status);
        var remaining = (await context.Categories.GetAsync(CancellationToken.None))!;
        Assert.DoesNotContain(remaining, item => item.Id == category.Id);

        var updated = await context.Entries.UpdateAsync(
            entry.Result!.Entry.Id,
            new UpdateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 50m, Today, "晚餐", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Success, updated.Status);
        Assert.Equal("餐饮", updated.Result!.Entry.Category);
    }

    [Fact]
    public async Task SalaryCategory_CannotBeDeleted()
    {
        var context = await CreateAsync();
        var salary = (await context.Categories.GetAsync(CancellationToken.None))!
            .Single(item => item.SystemKey == "salary");

        var result = await context.Categories.DeleteAsync(salary.Id, CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("不能删除"));
    }

    [Fact]
    public async Task Reorder_UpdatesSortOrderForAllCategoriesOfKind()
    {
        var context = await CreateAsync();
        var expense = (await context.Categories.GetAsync(CancellationToken.None))!
            .Where(category => category.Kind == LedgerEntryKind.Expense)
            .ToList();
        var reversed = expense.Select(category => category.Id).Reverse().ToList();

        var result = await context.Categories.ReorderAsync(
            new ReorderCategoriesRequest(LedgerEntryKind.Expense, reversed),
            CancellationToken.None);

        Assert.Equal(CategoryOperationStatus.Success, result.Status);
        var reordered = (await context.Categories.GetAsync(CancellationToken.None))!
            .Where(category => category.Kind == LedgerEntryKind.Expense)
            .OrderBy(category => category.SortOrder)
            .Select(category => category.Id)
            .ToList();
        Assert.Equal(reversed, reordered);
    }

    private static async Task<TestContext> CreateAsync()
    {
        var repository = new FakeLedgerRepository();
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(new DateOnly(2026, 10, 1), 10000m),
            CancellationToken.None);

        return new TestContext(
            repository,
            new CategoryService(repository, dates),
            new LedgerEntryService(repository, dates));
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        CategoryService Categories,
        LedgerEntryService Entries);
}
