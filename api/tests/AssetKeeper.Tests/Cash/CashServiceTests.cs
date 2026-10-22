using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Cash;

public sealed class CashServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    [Fact]
    public async Task GetOverviewAsync_WithoutLedger_ReturnsNull()
    {
        var repository = new FakeLedgerRepository();
        var service = CreateCashService(repository);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Null(overview);
    }

    [Fact]
    public async Task GetOverviewAsync_AfterInitialization_ReturnsOpeningBalance()
    {
        var context = await CreateContextAsync();

        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(1000m, overview.Balance);
        Assert.Equal(BusinessToday, overview.AsOfDate);
        Assert.Equal(new DateOnly(2026, 10, 1), overview.From);
        Assert.Equal(new DateOnly(2026, 10, 31), overview.To);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(0m, overview.Inflow);
        Assert.Equal(0m, overview.Outflow);
        Assert.Equal(0m, overview.AdjustmentTotal);
        Assert.Equal(0m, overview.NetChange);
        Assert.Empty(overview.Activities);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEntries_ReportsFlowSeparatelyFromAdjustments()
    {
        var context = await CreateContextAsync();

        await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Income, "工资", 500m),
            CancellationToken.None);
        await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Expense, "餐饮", 35.60m, "午餐"),
            CancellationToken.None);

        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(1464.40m, overview.Balance);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(500m, overview.Inflow);
        Assert.Equal(35.60m, overview.Outflow);
        Assert.Equal(0m, overview.AdjustmentTotal);
        Assert.Equal(464.40m, overview.NetChange);
        Assert.Equal(2, overview.Activities.Count);
        Assert.All(overview.Activities, activity => Assert.Equal(CashActivityType.Daily, activity.Type));
        Assert.Contains(overview.Activities, activity => activity.Amount == 500m);
        Assert.Contains(overview.Activities, activity => activity.Amount == -35.60m && activity.Note == "午餐");
    }

    [Fact]
    public async Task GetOverviewAsync_WithPriorMonthEntries_IncludesThemInOpeningBalance()
    {
        var context = await CreateContextAsync(
            openingBalance: 1000m,
            startDate: new DateOnly(2026, 9, 11));

        await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Expense, "餐饮", 100m, businessDate: new DateOnly(2026, 9, 30)),
            CancellationToken.None);
        await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Income, "奖金", 50m),
            CancellationToken.None);

        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(950m, overview.Balance);
        Assert.Equal(900m, overview.OpeningBalance);
        Assert.Equal(50m, overview.Inflow);
        Assert.Equal(0m, overview.Outflow);
        Assert.Equal(50m, overview.NetChange);
        Assert.Single(overview.Activities);
    }

    [Fact]
    public async Task ReconcileAsync_WithDifference_CreatesAdjustmentAndUpdatesCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(950m, "漏记支出", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.False(result.Result.IsBalanced);
        Assert.Equal(-50m, result.Result.Difference);
        Assert.Equal(950m, result.Result.CashBalance);
        Assert.NotNull(result.Result.Adjustment);
        Assert.Equal(1000m, result.Result.Adjustment.PreviousBalance);
        Assert.Equal(950m, result.Result.Adjustment.NewBalance);
        Assert.Equal(-50m, result.Result.Adjustment.Difference);
        Assert.Equal("漏记支出", result.Result.Adjustment.Reason);
        Assert.Single(context.Repository.Adjustments);

        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);
        var ledgerOverview = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(950m, overview.Balance);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(-50m, overview.AdjustmentTotal);
        Assert.Equal(-50m, overview.NetChange);
        var activity = Assert.Single(overview.Activities);
        Assert.Equal(CashActivityType.Adjustment, activity.Type);
        Assert.Equal("余额调整", activity.Title);
        Assert.Equal(-50m, activity.Amount);
        Assert.NotNull(ledgerOverview);
        Assert.Equal(950m, ledgerOverview.TotalAssets);
        Assert.False(ledgerOverview.Month.HasRecords);
        Assert.Equal(0m, ledgerOverview.Month.Expense);
    }

    [Fact]
    public async Task ReconcileAsync_UpdatesAsOfDateToBusinessToday()
    {
        var context = await CreateContextAsync(startDate: new DateOnly(2026, 10, 1));

        await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(900m, "首次核对", Guid.NewGuid()),
            CancellationToken.None);
        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(BusinessToday, overview.AsOfDate);
    }

    [Fact]
    public async Task ReconcileAsync_WithEqualBalance_ReturnsBalancedWithoutAdjustment()
    {
        var context = await CreateContextAsync();

        var result = await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(1000m, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.True(result.Result.IsBalanced);
        Assert.Equal(0m, result.Result.Difference);
        Assert.Equal(1000m, result.Result.CashBalance);
        Assert.Null(result.Result.Adjustment);
        Assert.Empty(context.Repository.Adjustments);
        Assert.Equal(2, context.Repository.SaveCount);
        Assert.Single(context.Repository.MutationReceipts);
    }

    [Fact]
    public async Task ReconcileAsync_WithDifferenceAndBlankReason_ReturnsInvalid()
    {
        var context = await CreateContextAsync();

        var result = await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(950m, "  ", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Invalid, result.Status);
        Assert.Contains("调整原因不能为空。", result.Errors);
        Assert.Empty(context.Repository.Adjustments);
    }

    [Theory]
    [InlineData(-0.01, "核对后的现金余额不能为负数。")]
    [InlineData(10.001, "核对后的现金余额最多保留两位小数。")]
    [InlineData(1000000000.01, "核对后的现金余额超出允许范围。")]
    public async Task ReconcileAsync_WithInvalidBalance_ReturnsInvalid(decimal balance, string expectedError)
    {
        var context = await CreateContextAsync();

        var result = await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(balance, "核对", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Invalid, result.Status);
        Assert.Contains(expectedError, result.Errors);
    }

    [Fact]
    public async Task ReconcileAsync_WithoutLedger_ReturnsNotFound()
    {
        var service = CreateCashService(new FakeLedgerRepository());

        var result = await service.ReconcileAsync(
            new ReconcileCashRequest(100m, "核对", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(CashReconcileStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateAsync_AfterReconciliation_UsesAdjustedBalance()
    {
        var context = await CreateContextAsync();

        await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(950m, "漏记支出", Guid.NewGuid()),
            CancellationToken.None);

        var rejected = await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Expense, "购物", 951m),
            CancellationToken.None);
        var accepted = await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Expense, "购物", 950m),
            CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Invalid, rejected.Status);
        Assert.Contains(rejected.Errors, error => error.Contains("现金余额不足"));
        Assert.Equal(EntryOperationStatus.Created, accepted.Status);
        Assert.NotNull(accepted.Result);
        Assert.Equal(0m, accepted.Result.CashBalance);
    }

    [Fact]
    public async Task GetOverviewAsync_AfterEntryUpdateAndReverse_ReflectsCurrentCashOnly()
    {
        var context = await CreateContextAsync();
        var created = await context.Entries.CreateAsync(
            EntryRequest(LedgerEntryKind.Expense, "餐饮", 100m),
            CancellationToken.None);

        await context.Entries.UpdateAsync(
            created.Result!.Entry.Id,
            new UpdateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 150m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Entries.ReverseAsync(
            created.Result.Entry.Id,
            new ReverseLedgerEntryRequest("重复记录", Guid.NewGuid()),
            CancellationToken.None);

        var overview = await context.Cash.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(1000m, overview.Balance);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(0m, overview.Inflow);
        Assert.Equal(0m, overview.Outflow);
        Assert.Equal(0m, overview.NetChange);
        Assert.Empty(overview.Activities);
    }

    private static async Task<TestContext> CreateContextAsync(
        decimal openingBalance = 1000m,
        DateOnly? startDate = null)
    {
        var repository = new FakeLedgerRepository();
        var businessDateProvider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, businessDateProvider);

        await ledgerService.InitializeAsync(
            new InitializeLedgerRequest(startDate ?? BusinessToday, openingBalance),
            CancellationToken.None);

        return new TestContext(
            repository,
            ledgerService,
            new LedgerEntryService(repository, businessDateProvider),
            new CashService(repository, businessDateProvider));
    }

    private static CashService CreateCashService(FakeLedgerRepository repository)
    {
        return new CashService(repository, new BusinessDateProvider(new FixedTimeProvider(Now)));
    }

    private static CreateLedgerEntryRequest EntryRequest(
        LedgerEntryKind kind,
        string category,
        decimal amount,
        string? note = null,
        DateOnly? businessDate = null)
    {
        return new CreateLedgerEntryRequest(
            kind,
            category,
            amount,
            businessDate ?? BusinessToday,
            note,
            Guid.NewGuid());
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        LedgerService Ledger,
        LedgerEntryService Entries,
        CashService Cash);
}
