using AssetKeeper.Tests.Quotes;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Application.Stocks;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Trend;

public sealed class TrendServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllRange_ReturnsDailyCoverageAndLeavesIncompleteDatesEmpty()
    {
        await using var context = await CreateAsync();
        var trend = await context.Trend.GetAsync(TrendRange.All, CancellationToken.None);

        Assert.NotNull(trend);
        Assert.Equal(Today.AddDays(-60), trend.From);
        Assert.Equal(Today, trend.To);
        Assert.Equal(61, trend.Points.Count);

        var beforeProvident = trend.Points.Single(point => point.Date == Today.AddDays(-20));
        Assert.Null(beforeProvident.Provident);
        Assert.Null(beforeProvident.TotalAssets);
        Assert.False(beforeProvident.IsComplete);
        Assert.False(beforeProvident.HasProvident);
        Assert.True(beforeProvident.HasFunds);
        Assert.True(beforeProvident.HasGold);

        var latest = trend.Points.Single(point => point.Date == Today);
        Assert.True(latest.IsComplete);
        Assert.Equal(14000m, latest.TotalAssets);
        Assert.Equal(2000m, latest.Provident);
        Assert.Equal(1000m, latest.FundValue);
        Assert.Equal(9000m, latest.GoldValue);
        Assert.True(latest.HasCash);
        Assert.True(latest.HasProvident);
        Assert.True(latest.HasFunds);
        Assert.True(latest.HasGold);
        Assert.Equal(1000m, latest.StockValue);
        Assert.True(latest.HasStocks);
    }

    [Fact]
    public async Task Ranges_ClampToLedgerStartAndLimitDays()
    {
        await using var context = await CreateAsync();
        var week = await context.Trend.GetAsync(TrendRange.Week, CancellationToken.None);
        var month = await context.Trend.GetAsync(TrendRange.Month, CancellationToken.None);
        var quarter = await context.Trend.GetAsync(TrendRange.Quarter, CancellationToken.None);

        Assert.NotNull(week);
        Assert.NotNull(month);
        Assert.NotNull(quarter);
        Assert.Equal(Today.AddDays(-6), week.From);
        Assert.Equal(7, week.Points.Count);
        Assert.Equal(Today.AddDays(-29), month.From);
        Assert.Equal(30, month.Points.Count);
        Assert.Equal(Today.AddDays(-60), quarter.From);
        Assert.Equal(61, quarter.Points.Count);
    }

    private static async Task<TestContext> CreateAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new(Today.AddDays(-60), 1000m),
            CancellationToken.None);
        await TestFundQuotes.CreatePositionAsync(repository, dates,
            new("测试基金", "000001", "A", "测试", 100m, 1000m, 10m, Today.AddDays(-50), null, Guid.NewGuid(),
                HoldingDate: Today.AddDays(-50)),
            CancellationToken.None);
        await new GoldService(repository, dates).SaveHoldingAsync(
            new(10m, 8000m, 900m, Today.AddDays(-50), null, Guid.NewGuid(), HoldingDate: Today.AddDays(-50)),
            CancellationToken.None);
        await new StockService(repository, dates).SaveHoldingAsync(
            new SaveStockHoldingRequest(
                StockMarket.Shanghai,
                "600519",
                "贵州茅台",
                10m,
                1000m,
                100m,
                Today.AddDays(-50),
                Today.AddDays(-50),
                null,
                Guid.NewGuid()),
            CancellationToken.None);
        await new ProvidentFundService(repository, dates).DepositAsync(
            new(2000m, Today.AddDays(-10), "缴存", Guid.NewGuid()),
            CancellationToken.None);

        return new TestContext(db, new TrendService(repository, dates));
    }

    private sealed record TestContext(AppDbContext Db, TrendService Trend) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
