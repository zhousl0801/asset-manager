using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Assets;

public sealed class FundServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    [Fact]
    public async Task CreatePositionAsync_CreatesHoldingWithoutChangingCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Funds.CreatePositionAsync(PositionRequest(), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(100m, result.Result.Position.Shares);
        Assert.Equal(1000m, result.Result.Position.Cost);
        Assert.Equal(1200m, result.Result.Position.Value);
        Assert.Equal(200m, result.Result.Position.HoldingProfit);
        Assert.Equal(10000m, context.Repository.CashAccount!.Balance);
        Assert.Single(context.Repository.FundPositions);
        Assert.Equal(FundActivityType.Opening, result.Result.Activity.Type);
    }

    [Fact]
    public async Task BuyAsync_ReducesCashAndAddsSharesAndCost()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(
                position.Id,
                10m,
                130m,
                13m,
                BusinessToday,
                BusinessToday,
                "加仓",
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(110m, result.Result.Position.Shares);
        Assert.Equal(1130m, result.Result.Position.Cost);
        Assert.Equal(1430m, result.Result.Position.Value);
        Assert.Equal(9870m, result.Result.CashBalance);
        Assert.Equal(-130m, result.Result.Activity.CashEffect);
    }

    [Fact]
    public async Task BuyAsync_WithDuplicateRequestId_DoesNotBuyTwice()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var requestId = Guid.NewGuid();
        var request = new BuyFundRequest(
            position.Id,
            10m,
            130m,
            13m,
            BusinessToday,
            BusinessToday,
            null,
            requestId);

        var first = await context.Funds.BuyAsync(request, CancellationToken.None);
        var second = await context.Funds.BuyAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(110m, second.Result.Position.Shares);
        Assert.Equal(9870m, second.Result.CashBalance);
        Assert.Equal(110m, context.Repository.FundPositions[0].Shares);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task SellAsync_ReducesSharesAndCostAndAddsNetCash()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        await context.Funds.BuyAsync(
            new BuyFundRequest(position.Id, 10m, 130m, 13m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Funds.SellAsync(
            new SellFundRequest(position.Id, 10m, 150m, BusinessToday, "赎回", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(100m, result.Result.Position.Shares);
        Assert.Equal(1027.27m, result.Result.Position.Cost);
        Assert.Equal(1300m, result.Result.Position.Value);
        Assert.Equal(10020m, result.Result.CashBalance);
        Assert.Equal(47.27m, result.Result.Position.RealizedProfit);
    }

    [Fact]
    public async Task SellAsync_WithMoreThanHeld_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.SellAsync(
            new SellFundRequest(position.Id, 101m, 200m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("超卖"));
        Assert.Equal(100m, context.Repository.FundPositions[0].Shares);
    }

    [Fact]
    public async Task BuyAsync_WithInsufficientCash_ReturnsInvalid()
    {
        var context = await CreateContextAsync(100m);
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(position.Id, 1m, 101m, 12m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("现金余额不足"));
        Assert.Equal(100m, context.Repository.FundPositions[0].Shares);
    }

    [Fact]
    public async Task UpdateQuoteAsync_ChangesValueWithoutCashOrCost()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.UpdateQuoteAsync(
            new UpdateFundQuoteRequest(position.Id, 14m, BusinessToday, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(1400m, result.Result.Position.Value);
        Assert.Equal(1000m, result.Result.Position.Cost);
        Assert.Equal(10000m, context.Repository.CashAccount!.Balance);
    }

    [Fact]
    public async Task BuyAsync_WithUnknownPosition_ReturnsNotFound()
    {
        var context = await CreateContextAsync();

        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(Guid.NewGuid(), 1m, 10m, 10m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreatePositionAsync_WithFutureValueDate_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var request = PositionRequest() with { NetValueDate = BusinessToday.AddDays(1) };

        var result = await context.Funds.CreatePositionAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Empty(context.Repository.FundPositions);
    }

    [Fact]
    public async Task CashAndHomeOverview_ReflectFundBuyAndSell()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        await context.Funds.BuyAsync(
            new BuyFundRequest(position.Id, 10m, 130m, 13m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Funds.SellAsync(
            new SellFundRequest(position.Id, 10m, 150m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var cash = await context.Cash.GetOverviewAsync(CancellationToken.None);
        var home = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(cash);
        Assert.Equal(10020m, cash.Balance);
        Assert.Equal(150m, cash.Inflow);
        Assert.Equal(130m, cash.Outflow);
        Assert.Equal(20m, cash.NetChange);
        Assert.Equal(2, cash.Activities.Count);
        Assert.All(cash.Activities, activity => Assert.Equal(CashActivityType.AssetTransfer, activity.Type));
        Assert.Contains(cash.Activities, activity => activity.Title == "基金买入");
        Assert.Contains(cash.Activities, activity => activity.Title == "基金卖出");
        Assert.NotNull(home);
        Assert.True(home.Fund.IsConfigured);
        Assert.Equal(1300m, home.Fund.Amount);
        Assert.Equal(11320m, home.TotalAssets);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutLedger_ReturnsNull()
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var service = new FundService(repository, provider);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Null(overview);
    }

    private static async Task<FundPosition> CreatePositionAsync(TestContext context)
    {
        var result = await context.Funds.CreatePositionAsync(PositionRequest(), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);

        return context.Repository.FundPositions.Single();
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
            BusinessToday,
            "已有持仓",
            Guid.NewGuid(),
            HoldingDate: BusinessToday);
    }

    private static async Task<TestContext> CreateContextAsync(decimal cashBalance = 10000m)
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, provider);
        await ledgerService.InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, cashBalance),
            CancellationToken.None);

        return new TestContext(
            repository,
            ledgerService,
            new CashService(repository, provider),
            new FundService(repository, provider));
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        LedgerService Ledger,
        CashService Cash,
        FundService Funds);
}
