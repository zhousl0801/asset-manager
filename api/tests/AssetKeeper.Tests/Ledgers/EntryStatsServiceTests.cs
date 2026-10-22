using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Ledgers;

public sealed class EntryStatsServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MonthStats_ReturnDailyPointsTotalsAveragesAndCategoryShare()
    {
        var context = await CreateAsync(new DateOnly(2026, 9, 1));
        await CreateEntryAsync(context, LedgerEntryKind.Income, "工资", 1000m, new DateOnly(2026, 10, 1));
        await CreateEntryAsync(context, LedgerEntryKind.Expense, "餐饮", 100m, new DateOnly(2026, 10, 5));
        await CreateEntryAsync(context, LedgerEntryKind.Expense, "购物", 200m, new DateOnly(2026, 10, 5));
        await CreateEntryAsync(context, LedgerEntryKind.Income, "奖金", 500m, new DateOnly(2026, 10, 10));

        var stats = await context.Stats.GetAsync(EntryPeriod.Month, null, CancellationToken.None);

        Assert.NotNull(stats);
        Assert.Equal(new DateOnly(2026, 10, 1), stats.From);
        Assert.Equal(new DateOnly(2026, 10, 31), stats.To);
        Assert.Equal(21, stats.CoveredDays);
        Assert.Equal(1500m, stats.Income);
        Assert.Equal(300m, stats.Expense);
        Assert.Equal(71.43m, stats.IncomeDailyAverage);
        Assert.Equal(14.29m, stats.ExpenseDailyAverage);
        Assert.Equal(21, stats.Points.Count);
        Assert.Equal(1000m, stats.Points[0].Income);
        var shoppingDay = stats.Points.Single(point => point.Date == new DateOnly(2026, 10, 5));
        Assert.Equal(300m, shoppingDay.Expense);

        Assert.Collection(
            stats.IncomeCategories,
            category =>
            {
                Assert.Equal("工资", category.Category);
                Assert.Equal(1000m, category.Amount);
                Assert.Equal(66.67m, category.Share);
            },
            category =>
            {
                Assert.Equal("奖金", category.Category);
                Assert.Equal(500m, category.Amount);
                Assert.Equal(33.33m, category.Share);
            });
        Assert.Collection(
            stats.ExpenseCategories,
            category =>
            {
                Assert.Equal("购物", category.Category);
                Assert.Equal(200m, category.Amount);
                Assert.Equal(66.67m, category.Share);
            },
            category =>
            {
                Assert.Equal("餐饮", category.Category);
                Assert.Equal(100m, category.Amount);
                Assert.Equal(33.33m, category.Share);
            });
        Assert.Null(stats.Note);
    }

    [Fact]
    public async Task MonthStats_WhenLedgerStartsMidPeriod_UseCoveredDaysAndExplainCoverage()
    {
        var context = await CreateAsync(new DateOnly(2026, 10, 10));
        await CreateEntryAsync(context, LedgerEntryKind.Expense, "餐饮", 120m, new DateOnly(2026, 10, 12));

        var stats = await context.Stats.GetAsync(EntryPeriod.Month, null, CancellationToken.None);

        Assert.NotNull(stats);
        Assert.Equal(12, stats.CoveredDays);
        Assert.Equal(10m, stats.ExpenseDailyAverage);
        Assert.Equal("账本自 2026-10-10 起，周期前段未覆盖。", stats.Note);
    }

    [Fact]
    public async Task YearStats_GroupByMonthAndCountOnlyElapsedMonths()
    {
        var context = await CreateAsync(new DateOnly(2026, 9, 1));
        await CreateEntryAsync(context, LedgerEntryKind.Income, "工资", 1000m, new DateOnly(2026, 9, 5));
        await CreateEntryAsync(context, LedgerEntryKind.Income, "工资", 1200m, new DateOnly(2026, 10, 5));

        var stats = await context.Stats.GetAsync(EntryPeriod.Year, new DateOnly(2026, 10, 21), CancellationToken.None);

        Assert.NotNull(stats);
        Assert.Equal(10, stats.Points.Count);
        Assert.Equal(1000m, stats.Points.Single(point => point.Date == new DateOnly(2026, 9, 1)).Income);
        Assert.Equal(1200m, stats.Points.Single(point => point.Date == new DateOnly(2026, 10, 1)).Income);
        Assert.Equal(2200m, stats.Income);
        Assert.Equal(51, stats.CoveredDays);
        Assert.Equal(43.14m, stats.IncomeDailyAverage);
        Assert.Equal("账本自 2026-09-01 起，周期前段未覆盖。", stats.Note);
    }

    private static Task CreateEntryAsync(
        TestContext context,
        LedgerEntryKind kind,
        string category,
        decimal amount,
        DateOnly date)
    {
        return context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(kind, category, amount, date, null, Guid.NewGuid()),
            CancellationToken.None);
    }

    private static async Task<TestContext> CreateAsync(DateOnly startDate)
    {
        var repository = new FakeLedgerRepository();
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(startDate, 10000m),
            CancellationToken.None);

        return new TestContext(
            new LedgerEntryService(repository, dates),
            new EntryStatsService(repository, dates));
    }

    private sealed record TestContext(
        LedgerEntryService Entries,
        EntryStatsService Stats);
}
