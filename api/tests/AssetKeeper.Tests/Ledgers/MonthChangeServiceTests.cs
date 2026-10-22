using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Ledgers;

public sealed class MonthChangeServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_ExplainsTotalChangeWithFourComponents()
    {
        await using var context = await CreateAsync(Today.AddDays(-31));

        await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Income, "工资", 2000m, new DateOnly(2026, 10, 5), null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Expense, "餐饮", 500m, new DateOnly(2026, 10, 6), null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Provident.DepositAsync(
            new ProvidentDepositRequest(1000m, new DateOnly(2026, 10, 10), null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Funds.UpdateQuoteAsync(
            new UpdateFundQuoteRequest(context.FundPositionId, 12m, new DateOnly(2026, 10, 15), Guid.NewGuid()),
            CancellationToken.None);
        await context.Gold.UpdateQuoteAsync(
            new UpdateGoldQuoteRequest(950m, new DateOnly(2026, 10, 16), Guid.NewGuid()),
            CancellationToken.None);
        var cash = await context.Cash.GetOverviewAsync(CancellationToken.None);
        await context.Cash.ReconcileAsync(
            new ReconcileCashRequest(11600m, "漏记收入", Guid.NewGuid(), cash!.Version),
            CancellationToken.None);

        var change = await context.MonthChange.GetAsync(CancellationToken.None);

        Assert.NotNull(change);
        Assert.True(change.IsComplete);
        Assert.Equal(new DateOnly(2026, 10, 1), change.From);
        Assert.Equal(Today, change.To);
        Assert.Equal(3300m, change.TotalChange);
        Assert.Equal(1500m, change.LivingSurplus);
        Assert.Equal(1000m, change.ProvidentDeposit);
        Assert.Equal(100m, change.ReconciliationDifference);
        Assert.Equal(700m, change.InvestmentProfit);
        Assert.Equal(
            change.TotalChange,
            change.LivingSurplus + change.ProvidentDeposit + change.InvestmentProfit + change.ReconciliationDifference);
        Assert.Empty(change.Notes);
    }

    [Fact]
    public async Task GetAsync_WhenLedgerStartsThisMonth_UsesStartDateAndExplainsIt()
    {
        var startDate = new DateOnly(2026, 10, 10);
        await using var context = await CreateAsync(startDate);
        await context.Entries.CreateAsync(
            new CreateLedgerEntryRequest(LedgerEntryKind.Income, "工资", 100m, new DateOnly(2026, 10, 12), null, Guid.NewGuid()),
            CancellationToken.None);

        var change = await context.MonthChange.GetAsync(CancellationToken.None);

        Assert.NotNull(change);
        Assert.True(change.IsComplete);
        Assert.Equal(startDate, change.From);
        Assert.Equal(100m, change.TotalChange);
        Assert.Equal(100m, change.LivingSurplus);
        Assert.Equal(0m, change.InvestmentProfit);
        Assert.Contains("本月变化自账本起始日起算。", change.Notes);
    }

    [Fact]
    public async Task GetAsync_WithoutCompleteAssetCoverage_LeavesChangeEmpty()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(Today.AddDays(-10), 1000m),
            CancellationToken.None);

        var change = await new MonthChangeService(repository, dates).GetAsync(CancellationToken.None);

        Assert.NotNull(change);
        Assert.False(change.IsComplete);
        Assert.Null(change.TotalChange);
        Assert.Null(change.InvestmentProfit);
        Assert.Contains("部分日期资产或行情依据不足，无法计算本月变化。", change.Notes);
        Assert.Equal(0m, change.LivingSurplus);
        Assert.Equal(0m, change.ReconciliationDifference);
        await db.DisposeAsync();
    }

    private static async Task<TestContext> CreateAsync(DateOnly startDate)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(startDate, 10000m),
            CancellationToken.None);
        var openingDate = startDate;
        var fund = await new FundService(repository, dates).CreatePositionAsync(
            new CreateFundPositionRequest(
                "测试基金",
                "000001",
                "A",
                "测试平台",
                100m,
                1000m,
                10m,
                openingDate,
                null,
                Guid.NewGuid(),
                HoldingDate: openingDate),
            CancellationToken.None);
        await new GoldService(repository, dates).SaveHoldingAsync(
            new SaveGoldHoldingRequest(10m, 8000m, 900m, openingDate, null, Guid.NewGuid(), HoldingDate: openingDate),
            CancellationToken.None);
        await new ProvidentFundService(repository, dates).SetOpeningAsync(
            new ProvidentOpeningRequest(5000m, openingDate, null, Guid.NewGuid()),
            CancellationToken.None);

        return new TestContext(
            db,
            new LedgerEntryService(repository, dates),
            new CashService(repository, dates),
            new FundService(repository, dates),
            new GoldService(repository, dates),
            new ProvidentFundService(repository, dates),
            new MonthChangeService(repository, dates),
            fund.Result!.Position.Id);
    }

    private sealed record TestContext(
        AppDbContext Db,
        LedgerEntryService Entries,
        CashService Cash,
        FundService Funds,
        GoldService Gold,
        ProvidentFundService Provident,
        MonthChangeService MonthChange,
        Guid FundPositionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
