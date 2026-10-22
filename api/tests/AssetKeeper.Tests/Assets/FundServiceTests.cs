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
    public async Task BuyAsync_ReducesCashAndRecordsPendingWithoutAddingShares()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(position.Id, 130m, BusinessToday, "加仓", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(100m, result.Result.Position.Shares);
        Assert.Equal(1000m, result.Result.Position.Cost);
        Assert.Equal(9870m, result.Result.CashBalance);
        Assert.Equal(-130m, result.Result.Activity.CashEffect);
        Assert.Equal(FundActivityType.BuyPending, result.Result.Activity.Type);
        Assert.Null(result.Result.Activity.Shares);

        var overview = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(130m, overview.PendingAmount);
        Assert.Single(overview.PendingBuys!);
        Assert.Equal(1330m, overview.TotalValue);
    }

    [Fact]
    public async Task ConfirmBuyAsync_MovesPendingIntoSharesWithoutSecondCashDeduction()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var pending = await BuyPendingAsync(context, position.Id, 130m);

        var result = await context.Funds.ConfirmBuyAsync(
            pending.Id,
            ConfirmRequest(10m, 13m, 0m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(110m, result.Result.Position.Shares);
        Assert.Equal(1130m, result.Result.Position.Cost);
        Assert.Equal(9870m, result.Result.CashBalance);
        Assert.Equal(0m, result.Result.Activity.CashEffect);
        Assert.Equal(FundActivityType.BuyConfirmed, result.Result.Activity.Type);
        Assert.Equal(0m, result.Result.Activity.Fee);

        var overview = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(0m, overview.PendingAmount);
        Assert.Empty(overview.PendingBuys!);
        Assert.Equal(1430m, overview.TotalValue);
    }

    [Fact]
    public async Task ConfirmBuyAsync_WithFee_KeepsPaidAmountAsCostAndRecordsFee()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var pending = await BuyPendingAsync(context, position.Id, 130m);

        var result = await context.Funds.ConfirmBuyAsync(
            pending.Id,
            ConfirmRequest(10m, 12.8m, 2m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(1130m, result.Result.Position.Cost);
        Assert.Equal(2m, result.Result.Activity.Fee);
    }

    [Fact]
    public async Task ConfirmBuyAsync_WithDuplicateRequestId_DoesNotConfirmTwice()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var pending = await BuyPendingAsync(context, position.Id, 130m);
        var request = ConfirmRequest(10m, 13m, 0m);

        var first = await context.Funds.ConfirmBuyAsync(pending.Id, request, CancellationToken.None);
        var second = await context.Funds.ConfirmBuyAsync(pending.Id, request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(110m, second.Result.Position.Shares);
        Assert.Equal(9870m, second.Result.CashBalance);
        Assert.Equal(1130m, context.Repository.FundPositions[0].Cost);
        Assert.Equal(3, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task ConfirmBuyAsync_WhenAlreadyConfirmed_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var pending = await BuyPendingAsync(context, position.Id, 130m);
        await context.Funds.ConfirmBuyAsync(pending.Id, ConfirmRequest(10m, 13m, 0m), CancellationToken.None);

        var result = await context.Funds.ConfirmBuyAsync(
            pending.Id,
            ConfirmRequest(10m, 13m, 0m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ConfirmBuyAsync_BeforeBuyDate_ReturnsInvalid()
    {
        var context = await CreateContextAsync(startDate: BusinessToday.AddDays(-5));
        var position = await CreatePositionAsync(context);
        var pending = await BuyPendingAsync(context, position.Id, 130m);

        var result = await context.Funds.ConfirmBuyAsync(
            pending.Id,
            ConfirmRequest(10m, 13m, 0m) with { BusinessDate = BusinessToday.AddDays(-1), NetValueDate = BusinessToday.AddDays(-1) },
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("确认日期不能早于买入日期"));
    }

    [Fact]
    public async Task ConfirmBuyAsync_WithUnknownPending_ReturnsNotFound()
    {
        var context = await CreateContextAsync();

        var result = await context.Funds.ConfirmBuyAsync(Guid.NewGuid(), ConfirmRequest(10m, 13m, 0m), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task BuyAsync_WithDuplicateRequestId_DoesNotBuyTwice()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var requestId = Guid.NewGuid();
        var request = new BuyFundRequest(position.Id, 130m, BusinessToday, null, requestId);

        var first = await context.Funds.BuyAsync(request, CancellationToken.None);
        var second = await context.Funds.BuyAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(100m, second.Result.Position.Shares);
        Assert.Equal(9870m, second.Result.CashBalance);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task SellFlow_FreezesSharesThenMovesProceedsToPendingArrivalAndCash()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        await BuyAndConfirmAsync(context, position.Id, 130m, 10m, 13m);

        var apply = await context.Funds.ApplySellAsync(
            new SellFundRequest(position.Id, 10m, BusinessToday, "赎回", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, apply.Status);
        Assert.NotNull(apply.Result);
        Assert.Equal(110m, apply.Result.Position.Shares);
        Assert.Equal(9870m, apply.Result.CashBalance);
        Assert.Equal(FundActivityType.SellApply, apply.Result.Activity.Type);

        var applied = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(applied);
        Assert.Single(applied.SellApplications!);
        Assert.Equal(10m, applied.SellApplications![0].Shares);

        var confirm = await context.Funds.ConfirmSellAsync(
            apply.Result.Activity.Id,
            new ConfirmFundSellRequest(10m, 13m, BusinessToday, 0m, 150m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, confirm.Status);
        Assert.NotNull(confirm.Result);
        Assert.Equal(100m, confirm.Result.Position.Shares);
        Assert.Equal(1027.27m, confirm.Result.Position.Cost);
        Assert.Equal(1300m, confirm.Result.Position.Value);
        Assert.Equal(9870m, confirm.Result.CashBalance);
        Assert.Equal(47.27m, confirm.Result.Position.RealizedProfit);
        Assert.Equal(FundActivityType.SellConfirm, confirm.Result.Activity.Type);

        var pending = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(pending);
        Assert.Empty(pending.SellApplications!);
        Assert.Single(pending.PendingArrivals!);
        Assert.Equal(150m, pending.PendingArrivalAmount);
        Assert.Equal(1450m, pending.TotalValue);

        var homeBeforeArrival = await context.Ledger.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(homeBeforeArrival);
        Assert.Equal(1450m, homeBeforeArrival.Fund.Amount);

        var arrival = await context.Funds.ArriveSellAsync(
            confirm.Result.Activity.Id,
            new FundSellArrivalRequest(150m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, arrival.Status);
        Assert.NotNull(arrival.Result);
        Assert.Equal(10020m, arrival.Result.CashBalance);
        Assert.Equal(FundActivityType.SellArrival, arrival.Result.Activity.Type);

        var arrived = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(arrived);
        Assert.Empty(arrived.PendingArrivals!);
        Assert.Equal(0m, arrived.PendingArrivalAmount);
        Assert.Equal(1300m, arrived.TotalValue);
    }

    [Fact]
    public async Task ApplySellAsync_WithMoreThanHeld_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.ApplySellAsync(
            new SellFundRequest(position.Id, 101m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("超过"));
        Assert.Equal(100m, context.Repository.FundPositions[0].Shares);
    }

    [Fact]
    public async Task ConfirmSellAsync_WithMoreThanApplied_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var apply = await context.Funds.ApplySellAsync(
            new SellFundRequest(position.Id, 10m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Funds.ConfirmSellAsync(
            apply.Result!.Activity.Id,
            new ConfirmFundSellRequest(11m, 10m, BusinessToday, 0m, 110m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("确认份额不能超过"));
    }

    [Fact]
    public async Task ArriveSellAsync_WithDifferentAmount_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var apply = await context.Funds.ApplySellAsync(
            new SellFundRequest(position.Id, 10m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        var confirm = await context.Funds.ConfirmSellAsync(
            apply.Result!.Activity.Id,
            new ConfirmFundSellRequest(10m, 10m, BusinessToday, 0m, 100m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Funds.ArriveSellAsync(
            confirm.Result!.Activity.Id,
            new FundSellArrivalRequest(99m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("到账金额必须与待到账金额一致"));
    }

    [Fact]
    public async Task ConfirmSellAsync_WithFewerSharesThanApplied_UnfreezesRemainder()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var apply = await context.Funds.ApplySellAsync(
            new SellFundRequest(position.Id, 30m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var confirm = await context.Funds.ConfirmSellAsync(
            apply.Result!.Activity.Id,
            new ConfirmFundSellRequest(20m, 10m, BusinessToday, 0m, 200m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, confirm.Status);
        Assert.Equal(80m, confirm.Result!.Position.Shares);

        var overview = await context.Funds.GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Empty(overview.SellApplications!);
        Assert.Single(overview.PendingArrivals!);
        Assert.Equal(20m, overview.PendingArrivals![0].Shares);
        Assert.Equal(200m, overview.PendingArrivalAmount);
    }

    [Fact]
    public async Task SellFlow_WithDuplicateRequestIds_DoesNotRepeatAnyStep()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        var applyRequest = new SellFundRequest(position.Id, 10m, BusinessToday, null, Guid.NewGuid());

        var apply = await context.Funds.ApplySellAsync(applyRequest, CancellationToken.None);
        var duplicateApply = await context.Funds.ApplySellAsync(applyRequest, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, apply.Status);
        Assert.Equal(AssetOperationStatus.Success, duplicateApply.Status);
        Assert.Equal(100m, duplicateApply.Result!.Position.Shares);

        var confirmRequest = new ConfirmFundSellRequest(10m, 10m, BusinessToday, 0m, 100m, BusinessToday, null, Guid.NewGuid());
        var confirm = await context.Funds.ConfirmSellAsync(apply.Result!.Activity.Id, confirmRequest, CancellationToken.None);
        var duplicateConfirm = await context.Funds.ConfirmSellAsync(apply.Result.Activity.Id, confirmRequest, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, confirm.Status);
        Assert.Equal(AssetOperationStatus.Success, duplicateConfirm.Status);
        Assert.Equal(90m, duplicateConfirm.Result!.Position.Shares);

        var arrivalRequest = new FundSellArrivalRequest(100m, BusinessToday, null, Guid.NewGuid());
        var arrival = await context.Funds.ArriveSellAsync(confirm.Result!.Activity.Id, arrivalRequest, CancellationToken.None);
        var duplicateArrival = await context.Funds.ArriveSellAsync(confirm.Result.Activity.Id, arrivalRequest, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, arrival.Status);
        Assert.Equal(AssetOperationStatus.Success, duplicateArrival.Status);
        Assert.Equal(10100m, duplicateArrival.Result!.CashBalance);
        Assert.Equal(4, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task BuyAsync_WithInsufficientCash_ReturnsInvalid()
    {
        var context = await CreateContextAsync(100m);
        var position = await CreatePositionAsync(context);

        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(position.Id, 101m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("现金余额不足"));
        Assert.Equal(100m, context.Repository.FundPositions[0].Shares);
        Assert.Single(context.Repository.AssetTransactions);
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
            new BuyFundRequest(Guid.NewGuid(), 10m, BusinessToday, null, Guid.NewGuid()),
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
        await BuyAndConfirmAsync(context, position.Id, 130m, 10m, 13m);
        await SellAndArriveAsync(context, position.Id, 10m, 13m, 150m);

        var cash = await context.Cash.GetOverviewAsync(CancellationToken.None);
        var home = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(cash);
        Assert.Equal(10020m, cash.Balance);
        Assert.Equal(150m, cash.Inflow);
        Assert.Equal(130m, cash.Outflow);
        Assert.Equal(20m, cash.NetChange);
        Assert.Equal(2, cash.Activities.Count);
        Assert.All(cash.Activities, activity => Assert.Equal(CashActivityType.AssetTransfer, activity.Type));
        Assert.Contains(cash.Activities, activity => activity.Title == "基金买入待确认");
        Assert.Contains(cash.Activities, activity => activity.Title == "基金赎回到账");
        Assert.NotNull(home);
        Assert.True(home.Fund.IsConfigured);
        Assert.Equal(1300m, home.Fund.Amount);
        Assert.Equal(11320m, home.TotalAssets);
    }

    [Fact]
    public async Task HomeOverview_IncludesPendingBuyWithoutChangingTotalAssets()
    {
        var context = await CreateContextAsync();
        var position = await CreatePositionAsync(context);
        await BuyPendingAsync(context, position.Id, 130m);

        var home = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(home);
        Assert.Equal(1330m, home.Fund.Amount);
        Assert.Equal(11200m, home.TotalAssets);
        Assert.Contains("待确认", home.Fund.Note);
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

    private static async Task<FundActivityDto> BuyPendingAsync(TestContext context, Guid positionId, decimal amount)
    {
        var result = await context.Funds.BuyAsync(
            new BuyFundRequest(positionId, amount, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);

        return result.Result.Activity;
    }

    private static async Task BuyAndConfirmAsync(
        TestContext context,
        Guid positionId,
        decimal amount,
        decimal shares,
        decimal netValue)
    {
        var pending = await BuyPendingAsync(context, positionId, amount);
        var result = await context.Funds.ConfirmBuyAsync(
            pending.Id,
            ConfirmRequest(shares, netValue, 0m),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
    }

    private static async Task SellAndArriveAsync(
        TestContext context,
        Guid positionId,
        decimal shares,
        decimal netValue,
        decimal netAmount)
    {
        var apply = await context.Funds.ApplySellAsync(
            new SellFundRequest(positionId, shares, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Created, apply.Status);
        var confirm = await context.Funds.ConfirmSellAsync(
            apply.Result!.Activity.Id,
            new ConfirmFundSellRequest(shares, netValue, BusinessToday, 0m, netAmount, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Created, confirm.Status);
        var arrival = await context.Funds.ArriveSellAsync(
            confirm.Result!.Activity.Id,
            new FundSellArrivalRequest(netAmount, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Created, arrival.Status);
    }

    private static ConfirmFundBuyRequest ConfirmRequest(decimal shares, decimal netValue, decimal fee)
    {
        return new ConfirmFundBuyRequest(shares, netValue, BusinessToday, fee, BusinessToday, null, Guid.NewGuid());
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

    private static async Task<TestContext> CreateContextAsync(decimal cashBalance = 10000m, DateOnly? startDate = null)
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, provider);
        await ledgerService.InitializeAsync(
            new InitializeLedgerRequest(startDate ?? BusinessToday, cashBalance),
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
