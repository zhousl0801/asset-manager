using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Application.Stocks;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Assets;

public sealed class StockServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BuyAsync_CreatesPositionAndReducesCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Stocks.BuyAsync(
            BuyRequest(Today, 100m, 10m, 1m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(100m, result.Result.Position.Shares);
        Assert.Equal(1001m, result.Result.Position.Cost);
        Assert.Equal(1000m, result.Result.Position.Value);
        Assert.Equal(8999m, result.Result.CashBalance);
        Assert.Equal(StockActivityType.Buy, result.Result.Activity.Type);
        Assert.Equal(-1001m, result.Result.Activity.CashEffect);

        var overview = await context.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.True(overview.IsConfigured);
        Assert.Single(overview.Positions);
        Assert.Equal(1000m, overview.TotalValue);
        Assert.Equal(-1m, overview.TodayProfit);
        Assert.True(overview.TodayProfitComplete);
        Assert.Single(overview.Activities);
    }

    [Fact]
    public async Task BuyAsync_WithDuplicateRequestId_DoesNotBuyTwice()
    {
        var context = await CreateContextAsync();
        var request = BuyRequest(Today, 100m, 10m, 0m);

        var first = await context.Stocks.BuyAsync(request, CancellationToken.None);
        var second = await context.Stocks.BuyAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(100m, second.Result.Position.Shares);
        Assert.Equal(9000m, second.Result.CashBalance);
        Assert.Single(context.Repository.AssetTransactions);
    }

    [Fact]
    public async Task SellAsync_ReducesSharesCostAndAddsNetCash()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions;

        Assert.Single(position);

        var result = await context.Stocks.SellAsync(
            new SellStockRequest(position[0].Id, 40m, 12m, 2m, Today, "卖出", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(60m, result.Result.Position.Shares);
        Assert.Equal(600m, result.Result.Position.Cost);
        Assert.Equal(9478m, result.Result.CashBalance);
        Assert.Equal(StockActivityType.Sell, result.Result.Activity.Type);

        var overview = await context.Stocks.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(78m, overview.RealizedProfit);
    }

    [Fact]
    public async Task SellAsync_WithMoreThanHeld_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];

        var result = await context.Stocks.SellAsync(
            new SellStockRequest(position.Id, 101m, 10m, 0m, Today, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("超卖"));
        Assert.Equal(100m, context.Repository.StockPositions[0].Shares);
    }

    [Fact]
    public async Task BuyAsync_WithInsufficientCash_ReturnsInvalidAndRollsBackPosition()
    {
        var context = await CreateContextAsync(100m);

        var result = await context.Stocks.BuyAsync(
            BuyRequest(Today, 100m, 10m, 0m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("现金余额不足"));
        Assert.Empty(context.Repository.StockPositions);
    }

    [Fact]
    public async Task SaveHoldingAsync_RecordsExistingHoldingWithoutChangingCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Stocks.SaveHoldingAsync(
            new SaveStockHoldingRequest(
                StockMarket.Shanghai,
                "600519",
                "贵州茅台",
                100m,
                150000m,
                1600m,
                Today,
                Today,
                "已有持仓",
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(100m, result.Result.Position.Shares);
        Assert.Equal(150000m, result.Result.Position.Cost);
        Assert.Equal(160000m, result.Result.Position.Value);
        Assert.Equal(10000m, context.Repository.CashAccount!.Balance);
        Assert.Single(context.Repository.AssetTransactions);
        Assert.Equal(StockActivityType.Opening, result.Result.Activity.Type);
    }

    [Fact]
    public async Task SaveHoldingAsync_WithZeroSharesAndNonZeroCost_ReturnsInvalid()
    {
        var context = await CreateContextAsync();

        var result = await context.Stocks.SaveHoldingAsync(
            new SaveStockHoldingRequest(
                StockMarket.Shanghai,
                "600519",
                "贵州茅台",
                0m,
                100m,
                1600m,
                Today,
                Today,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Empty(context.Repository.StockPositions);
    }

    [Fact]
    public async Task RefreshQuoteAsync_UpdatesValueAndPreviousCloseWithoutChangingCost()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        var quotes = new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = 10m
        };

        var result = await context.WithQuotes(quotes).Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.True(result.Result.Updated);
        Assert.Equal(10.5m, result.Result.Price);
        Assert.Equal(10m, result.Result.PreviousClose);
        Assert.Equal(1000m, context.Repository.StockPositions[0].Cost);
        Assert.Equal(1050m, context.Repository.StockPositions[0].Value);
    }

    [Fact]
    public async Task GetOverviewAsync_CalculatesTodayProfitWithoutTodaysTrades()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        var withQuotes = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = 10m
        });
        await withQuotes.Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);

        var overview = await withQuotes.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(50m, overview.TodayProfit);
        Assert.True(overview.TodayProfitComplete);
        Assert.Equal(Today, overview.TodayProfitDate);
    }

    [Fact]
    public async Task GetOverviewAsync_CalculatesTodayProfitForTodaysBuyIncludingFee()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today, 100m, 10m, 1m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        var withQuotes = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = 10m
        });
        await withQuotes.Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);

        var overview = await withQuotes.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(49m, overview.TodayProfit);
        Assert.True(overview.TodayProfitComplete);
    }

    [Fact]
    public async Task GetOverviewAsync_CalculatesTodayProfitForTodaysSell()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        await context.Stocks.SellAsync(
            new SellStockRequest(position.Id, 100m, 10.5m, 10m, Today, null, Guid.NewGuid()),
            CancellationToken.None);
        var withQuotes = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = 10m
        });
        await withQuotes.Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);

        var overview = await withQuotes.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(40m, overview.TodayProfit);
        Assert.True(overview.TodayProfitComplete);
        Assert.Equal(40m, overview.RealizedProfit);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutPreviousClose_LeavesTodayProfitIncomplete()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        var withQuotes = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = null
        });
        await withQuotes.Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);

        var overview = await withQuotes.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Null(overview.TodayProfit);
        Assert.False(overview.TodayProfitComplete);
        Assert.Null(overview.Positions[0].TodayProfit);
        Assert.False(overview.Positions[0].TodayProfitComplete);
    }

    [Fact]
    public async Task GetOverviewAsync_WithTodaysCorrection_LeavesTodayProfitIncomplete()
    {
        var context = await CreateContextAsync();
        await context.Stocks.BuyAsync(
            BuyRequest(Today.AddDays(-2), 100m, 10m, 0m),
            CancellationToken.None);
        var position = context.Repository.StockPositions[0];
        var withQuotes = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 10.5m,
            PriceDate = Today,
            PreviousClose = 10m
        });
        await withQuotes.Stocks.RefreshQuoteAsync(position.Id, CancellationToken.None);
        var current = context.Repository.StockPositions[0];

        var correction = await withQuotes.Stocks.SaveHoldingAsync(
            new SaveStockHoldingRequest(
                StockMarket.Shanghai,
                current.Code,
                current.Name,
                100m,
                1000m,
                10.5m,
                Today,
                Today,
                "核对",
                Guid.NewGuid(),
                ExpectedVersion: current.Version,
                PositionId: current.Id),
            CancellationToken.None);

        Assert.True(correction.Status == AssetOperationStatus.Success, string.Join(" ", correction.Errors));

        var overview = await withQuotes.Stocks.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Null(overview.TodayProfit);
        Assert.False(overview.TodayProfitComplete);
        Assert.Null(overview.Positions[0].TodayProfit);
    }

    private static BuyStockRequest BuyRequest(DateOnly date, decimal shares, decimal price, decimal fee)
    {
        return new BuyStockRequest(
            StockMarket.Shanghai,
            "600519",
            "贵州茅台",
            shares,
            price,
            fee,
            date,
            null,
            Guid.NewGuid());
    }

    private static async Task<TestContext> CreateContextAsync(decimal cashBalance = 10000m)
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledger = new LedgerService(repository, provider);
        await ledger.InitializeAsync(
            new InitializeLedgerRequest(Today.AddDays(-10), cashBalance),
            CancellationToken.None);
        var stocks = new StockService(repository, provider);

        return new TestContext(repository, ledger, new CashService(repository, provider), stocks, provider);
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        LedgerService Ledger,
        CashService Cash,
        StockService Stocks,
        BusinessDateProvider Dates)
    {
        public TestContext WithQuotes(IStockQuoteProvider provider)
        {
            return this with { Stocks = new StockService(Repository, Dates, provider) };
        }
    }

    [Fact]
    public async Task GetQuoteAsync_ReturnsLatestPriceWithoutWritingAnything()
    {
        var context = await CreateContextAsync();
        var service = context.WithQuotes(new ScriptedStockProvider
        {
            Price = 2.435m,
            PriceDate = Today,
            PreviousClose = 2.5m
        }).Stocks;

        var result = await service.GetQuoteAsync(StockMarket.Shenzhen, " 159915 ", CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal("159915", result.Result.Code);
        Assert.Equal(2.435m, result.Result.Price);
        Assert.Equal(Today, result.Result.PriceDate);
        Assert.Equal(2.5m, result.Result.PreviousClose);
        Assert.Empty(context.Repository.AssetTransactions);
        Assert.Null(context.Repository.GoldHolding);
    }

    [Fact]
    public async Task GetQuoteAsync_WithoutProvider_ReportsUnavailable()
    {
        var context = await CreateContextAsync();

        var result = await context.Stocks.GetQuoteAsync(StockMarket.Shenzhen, "159915", CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Unavailable, result.Status);
        Assert.Contains("未配置股票行情服务", result.Errors[0]);
    }

    private sealed class ScriptedStockProvider : IStockQuoteProvider
    {
        public decimal Price { get; init; }
        public DateOnly PriceDate { get; init; }
        public decimal? PreviousClose { get; init; }

        public Task<ExternalStockQuote> GetLatestAsync(
            StockMarket market,
            string code,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExternalStockQuote(
                market,
                code,
                "贵州茅台",
                Price,
                PriceDate,
                PreviousClose,
                null,
                "test",
                Now));
        }

        public Task<IReadOnlyList<ExternalStockSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ExternalStockSearchResult>>([
                new ExternalStockSearchResult(StockMarket.Shanghai, "600519", "贵州茅台")
            ]);
        }
    }
}
