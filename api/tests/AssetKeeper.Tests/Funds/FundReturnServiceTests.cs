using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Funds;

public sealed class FundReturnServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MonthReturn_AttributesProfitToNavDatesAndSeparatesIntervalFromHolding()
    {
        await using var context = await CreateAsync(withClassC: false);

        var result = await context.Returns.GetAsync(TrendRange.Month, Today, null, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 10, 1), result.From);
        Assert.Equal(new DateOnly(2026, 10, 1), result.IntervalFrom);
        Assert.Equal(1100m, result.OpeningAssetValue);
        Assert.Equal(1500m, result.ClosingAssetValue);
        Assert.Equal(300m, result.NetTransfer);
        Assert.Equal(100m, result.IntervalProfit);
        Assert.Equal(200m, result.HoldingProfit);
        Assert.Equal(1, result.DisclosureDays);

        var quietDay = result.Points.Single(point => point.Date == new DateOnly(2026, 10, 5));
        Assert.Null(quietDay.DailyProfit);
        Assert.False(quietDay.HasDisclosure);
        Assert.Equal("notDisclosed", quietDay.Status);

        var quoteDay = result.Points.Single(point => point.Date == new DateOnly(2026, 10, 10));
        Assert.Equal(100m, quoteDay.DailyProfit);
        Assert.Equal(100m, quoteDay.CumulativeProfit);
        Assert.True(quoteDay.IsComplete);

        var pendingDay = result.Points.Single(point => point.Date == new DateOnly(2026, 10, 12));
        Assert.Null(pendingDay.DailyProfit);
        Assert.Equal(100m, pendingDay.CumulativeProfit);
        Assert.Equal(1500m, pendingDay.FundValue);

        var fund = Assert.Single(result.Funds);
        Assert.Equal(100m, fund.IntervalProfit);
        Assert.Equal(200m, fund.HoldingProfit);
        Assert.Contains("未披露净值的日期不计当日收益", string.Join(" ", result.Notes));
    }

    [Fact]
    public async Task AllRange_UsesEarliestCompleteCoverageAndIncludesOpeningValue()
    {
        await using var context = await CreateAsync(withClassC: false);

        var result = await context.Returns.GetAsync(TrendRange.All, Today, null, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 9, 1), result.From);
        Assert.Equal(new DateOnly(2026, 9, 20), result.IntervalFrom);
        Assert.Equal(1000m, result.OpeningAssetValue);
        Assert.Equal(1500m, result.ClosingAssetValue);
        Assert.Equal(200m, result.IntervalProfit);
        Assert.Contains("区间盈亏自 2026-09-20 起算", string.Join(" ", result.Notes));
    }

    [Fact]
    public async Task DayDetail_ShowsUndisclosedDayAsNullAndCalculatesDisclosureDay()
    {
        await using var context = await CreateAsync(withClassC: false);

        var quiet = await context.Returns.GetDayAsync(new DateOnly(2026, 10, 12), null, false, CancellationToken.None);
        var quote = await context.Returns.GetDayAsync(new DateOnly(2026, 10, 10), null, false, CancellationToken.None);

        Assert.NotNull(quiet);
        Assert.Null(quiet.TotalProfit);
        Assert.Equal("notDisclosed", quiet.Status);
        var quietRow = Assert.Single(quiet.Positions);
        Assert.Null(quietRow.DailyProfit);
        Assert.False(quietRow.HasDisclosure);
        Assert.Equal(1500m, quietRow.Value);

        Assert.NotNull(quote);
        Assert.Equal(100m, quote.TotalProfit);
        Assert.True(quote.IsComplete);
        var quoteRow = Assert.Single(quote.Positions);
        Assert.Equal(100m, quoteRow.DailyProfit);
        Assert.Equal(12m, quoteRow.NetValue);
    }

    [Fact]
    public async Task PartialClassDisclosure_MarksDayPartialAndOnlySumsDisclosedClasses()
    {
        await using var context = await CreateAsync(withClassC: true);
        await context.Funds.UpdateQuoteAsync(
            new UpdateFundQuoteRequest(context.ClassCPositionId, 13m, new DateOnly(2026, 10, 15), Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Returns.GetAsync(TrendRange.Month, Today, null, false, CancellationToken.None);

        Assert.NotNull(result);
        var quoteDay = result.Points.Single(point => point.Date == new DateOnly(2026, 10, 10));
        Assert.Equal(100m, quoteDay.DailyProfit);
        Assert.Equal(1, quoteDay.CoveredPositions);
        Assert.Equal(2, quoteDay.TotalPositions);
        Assert.False(quoteDay.IsComplete);
        Assert.Equal("partial", quoteDay.Status);

        var classDay = result.Points.Single(point => point.Date == new DateOnly(2026, 10, 15));
        Assert.Equal(150m, classDay.DailyProfit);
        Assert.Equal("partial", classDay.Status);
        Assert.Equal(2, result.Points.Single(point => point.Date == Today).TotalPositions);
    }

    [Fact]
    public async Task EmptyScope_ReturnsExplanationWithoutThrowing()
    {
        await using var context = await CreateAsync(withClassC: false);

        var result = await context.Returns.GetAsync(
            TrendRange.Month,
            Today,
            Guid.NewGuid(),
            false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Points);
        Assert.Contains("分组不存在或已删除。", result.Notes);
    }

    private static async Task<TestContext> CreateAsync(bool withClassC)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(new DateOnly(2026, 9, 1), 10000m),
            CancellationToken.None);
        var funds = new FundService(repository, dates);
        var opening = await funds.CreatePositionAsync(
            new CreateFundPositionRequest(
                "纳指基金",
                "000001",
                "A",
                "平台一",
                100m,
                1000m,
                10m,
                new DateOnly(2026, 9, 20),
                null,
                Guid.NewGuid(),
                HoldingDate: new DateOnly(2026, 9, 20)),
            CancellationToken.None);
        var classC = withClassC
            ? await funds.CreatePositionAsync(
                new CreateFundPositionRequest(
                    "纳指基金",
                    "000002",
                    "C",
                    "平台二",
                    50m,
                    500m,
                    10m,
                    new DateOnly(2026, 9, 20),
                    null,
                    Guid.NewGuid(),
                    HoldingDate: new DateOnly(2026, 9, 20)),
                CancellationToken.None)
            : null;
        await funds.UpdateQuoteAsync(
            new UpdateFundQuoteRequest(opening.Result!.Position.Id, 11m, new DateOnly(2026, 9, 30), Guid.NewGuid()),
            CancellationToken.None);
        await funds.UpdateQuoteAsync(
            new UpdateFundQuoteRequest(opening.Result.Position.Id, 12m, new DateOnly(2026, 10, 10), Guid.NewGuid()),
            CancellationToken.None);

        await funds.BuyAsync(
            new BuyFundRequest(opening.Result.Position.Id, 300m, new DateOnly(2026, 10, 12), null, Guid.NewGuid()),
            CancellationToken.None);

        return new TestContext(
            db,
            funds,
            new FundReturnService(repository, dates),
            classC?.Result?.Position.Id ?? Guid.Empty);
    }

    private sealed record TestContext(
        AppDbContext Db,
        FundService Funds,
        FundReturnService Returns,
        Guid ClassCPositionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
