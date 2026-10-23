using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Quotes;

public sealed class QuoteRefreshServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FundRefresh_StoresProviderSourceAndSkipsDuplicateQuote()
    {
        var quote = new ExternalFundQuote("000001", 1.5m, Today, "eastmoney", Now);
        var context = await CreateAsync(new StubFundProvider(quote));

        var position = await context.Funds.CreatePositionAsync(PositionRequest(), CancellationToken.None);
        var first = await context.Funds.RefreshQuoteAsync(position.Result!.Position.Id, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, first.Status);
        Assert.True(first.Result!.Updated);
        Assert.Equal("eastmoney", first.Result.Source);
        Assert.Equal(1.5m, first.Result.Position.NetValue);
        Assert.Equal("eastmoney", first.Result.Position.LastQuoteSource);
        Assert.Null(first.Result.Position.LastQuoteError);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);

        var latest = await context.Repository.GetLatestPriceObservationAsync(
            context.Repository.Ledger!.Id, AssetType.Fund, position.Result.Position.Id, CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal("eastmoney", latest.Source);

        var second = await context.Funds.RefreshQuoteAsync(position.Result.Position.Id, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.False(second.Result!.Updated);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task FundRefresh_FailureKeepsLastQuoteAndRecordsError()
    {
        var context = await CreateAsync(new StubFundProvider(null, new QuoteUnavailableException("净值服务不可用")));

        var position = await context.Funds.CreatePositionAsync(PositionRequest(), CancellationToken.None);
        var result = await context.Funds.RefreshQuoteAsync(position.Result!.Position.Id, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Unavailable, result.Status);
        Assert.Equal(12m, context.Repository.FundPositions[0].NetValue);
        Assert.Equal("净值服务不可用", context.Repository.FundPositions[0].LastQuoteError);
        Assert.Null(context.Repository.FundPositions[0].LastQuoteSource);
        Assert.Single(context.Repository.AssetTransactions);
    }

    [Fact]
    public async Task GoldRefresh_StoresReferenceSourceAndSyncStatus()
    {
        var quote = new ExternalGoldQuote(910m, Today, "sina_shfe_au0", Now);
        var context = await CreateAsync(null, new StubGoldProvider(quote));
        await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        var result = await context.Gold.RefreshQuoteAsync(CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.True(result.Result!.Updated);
        Assert.Equal(910m, result.Result.Quote);
        Assert.Equal("sina_shfe_au0", result.Result.Source);
        Assert.Equal(910m, context.Repository.GoldHolding!.Quote);
        Assert.Equal("sina_shfe_au0", context.Repository.GoldHolding.LastQuoteSource);
        Assert.Null(context.Repository.GoldHolding.LastQuoteError);
    }

    [Fact]
    public async Task GoldRefresh_FailureKeepsLastQuoteAndRecordsError()
    {
        var context = await CreateAsync(null, new StubGoldProvider(null, new QuoteUnavailableException("黄金参考行情不可用")));
        await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        var result = await context.Gold.RefreshQuoteAsync(CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Unavailable, result.Status);
        Assert.Equal(900m, context.Repository.GoldHolding!.Quote);
        Assert.Equal("黄金参考行情不可用", context.Repository.GoldHolding.LastQuoteError);
        Assert.Null(context.Repository.GoldHolding.LastQuoteSource);
    }

    [Fact]
    public async Task GoldRefresh_HidesAutomaticQuoteUpdatesFromHoldingRecords()
    {
        var quote = new ExternalGoldQuote(910m, Today, "sina_shfe_au0", Now);
        var context = await CreateAsync(null, new StubGoldProvider(quote));
        await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        var result = await context.Gold.RefreshQuoteAsync(CancellationToken.None);
        var overview = await context.Gold.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.True(result.Result!.Updated);
        Assert.NotNull(overview);
        Assert.DoesNotContain(overview.Activities, activity => activity.Type == GoldActivityType.Quote);
        Assert.Contains(context.Repository.AssetTransactions,
            transaction => transaction.Kind == AssetTransactionKind.GoldQuote);
    }

    [Fact]
    public async Task ManualGoldQuoteUpdate_StaysInHoldingRecords()
    {
        var context = await CreateAsync();
        await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        await context.Gold.UpdateQuoteAsync(
            new UpdateGoldQuoteRequest(920m, Today, Guid.NewGuid()),
            CancellationToken.None);
        var overview = await context.Gold.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Contains(overview.Activities, activity => activity.Type == GoldActivityType.Quote);
    }

    private static CreateFundPositionRequest PositionRequest()
    {
        return new CreateFundPositionRequest(
            "纳指基金",
            "000001",
            "A",
            "示例平台",
            100m,
            1000m,
            12m,
            Today.AddDays(-20),
            "已有持仓",
            Guid.NewGuid(),
            HoldingDate: Today.AddDays(-20));
    }

    private static async Task<TestContext> CreateAsync(
        IFundQuoteProvider? fundProvider = null,
        IGoldQuoteProvider? goldProvider = null)
    {
        var repository = new FakeLedgerRepository();
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(Today.AddMonths(-2), 10000m),
            CancellationToken.None);

        return new TestContext(
            repository,
            new FundService(repository, dates, fundProvider),
            new GoldService(repository, dates, goldProvider));
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        FundService Funds,
        GoldService Gold);

    private sealed class StubFundProvider : IFundQuoteProvider
    {
        private readonly ExternalFundQuote? _quote;
        private readonly Exception? _error;

        public StubFundProvider(ExternalFundQuote? quote, Exception? error = null)
        {
            _quote = quote;
            _error = error;
        }

        public Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken)
        {
            return _error is null ? Task.FromResult(_quote!) : Task.FromException<ExternalFundQuote>(_error);
        }
    }

    private sealed class StubGoldProvider : IGoldQuoteProvider
    {
        private readonly ExternalGoldQuote? _quote;
        private readonly Exception? _error;

        public StubGoldProvider(ExternalGoldQuote? quote, Exception? error = null)
        {
            _quote = quote;
            _error = error;
        }

        public Task<ExternalGoldQuote> GetLatestAsync(CancellationToken cancellationToken)
        {
            return _error is null ? Task.FromResult(_quote!) : Task.FromException<ExternalGoldQuote>(_error);
        }
    }
}
