using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Assets;

public sealed class GoldServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    [Fact]
    public async Task SaveHoldingAsync_CreatesExistingHoldingWithoutChangingCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, BusinessToday, "已有持仓", Guid.NewGuid(), HoldingDate: BusinessToday),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(30m, result.Result.Grams);
        Assert.Equal(25600m, result.Result.Cost);
        Assert.Equal(27000m, result.Result.Value);
        Assert.Equal(10000m, context.Repository.CashAccount!.Balance);
        Assert.Single(context.Repository.AssetTransactions);

        var overview = await context.Gold.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.True(overview.IsConfigured);
        Assert.Equal(900m, overview.Quote);
        Assert.Equal(1400m, overview.HoldingProfit);
        Assert.Equal(853.33m, overview.AverageCost);
        Assert.Single(overview.Activities);
        Assert.Equal(GoldActivityType.Correction, overview.Activities[0].Type);
    }

    [Fact]
    public async Task BuyAsync_ReducesCashAndAddsGramsAndCost()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);

        var result = await context.Gold.BuyAsync(
            new BuyGoldRequest(1m, 900m, 910m, BusinessToday, BusinessToday, "买入", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(31m, result.Result.Grams);
        Assert.Equal(26500m, result.Result.Cost);
        Assert.Equal(910m, result.Result.Quote);
        Assert.Equal(28210m, result.Result.Value);
        Assert.Equal(9100m, result.Result.CashBalance);
        Assert.Equal(GoldActivityType.Buy, result.Result.Activity.Type);
        Assert.Equal(-900m, result.Result.Activity.CashEffect);
    }

    [Fact]
    public async Task BuyAsync_WithDuplicateRequestId_DoesNotBuyTwice()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);
        var requestId = Guid.NewGuid();
        var request = new BuyGoldRequest(
            1m,
            900m,
            910m,
            BusinessToday,
            BusinessToday,
            null,
            requestId);

        var first = await context.Gold.BuyAsync(request, CancellationToken.None);
        var second = await context.Gold.BuyAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(31m, second.Result.Grams);
        Assert.Equal(9100m, second.Result.CashBalance);
        Assert.Equal(31m, context.Repository.GoldHolding!.Grams);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task SellAsync_ReducesGramsCostAndAddsNetCash()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);
        await context.Gold.BuyAsync(
            new BuyGoldRequest(1m, 900m, 910m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Gold.SellAsync(
            new SellGoldRequest(1m, 898m, BusinessToday, "卖出", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(30m, result.Result.Grams);
        Assert.Equal(25645.16m, result.Result.Cost);
        Assert.Equal(9998m, result.Result.CashBalance);
        Assert.Equal(27300m, result.Result.Value);

        var overview = await context.Gold.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(43.16m, overview.RealizedProfit);
    }

    [Fact]
    public async Task SellAsync_WithMoreThanHeld_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);

        var result = await context.Gold.SellAsync(
            new SellGoldRequest(31m, 1000m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("超卖"));
        Assert.Equal(30m, context.Repository.GoldHolding!.Grams);
    }

    [Fact]
    public async Task BuyAsync_WithInsufficientCash_ReturnsInvalid()
    {
        var context = await CreateContextAsync(100m);

        var result = await context.Gold.BuyAsync(
            new BuyGoldRequest(1m, 101m, 900m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("现金余额不足"));
        Assert.Null(context.Repository.GoldHolding);
    }

    [Fact]
    public async Task UpdateQuoteAsync_ChangesValueWithoutCashOrCost()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);

        var result = await context.Gold.UpdateQuoteAsync(
            new UpdateGoldQuoteRequest(920m, BusinessToday, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(27600m, result.Result.Value);
        Assert.Equal(10000m, context.Repository.CashAccount!.Balance);
        Assert.Equal(25600m, context.Repository.GoldHolding!.Cost);
    }

    [Fact]
    public async Task UpdateQuoteAsync_WithoutHolding_CreatesConfiguredZeroHolding()
    {
        var context = await CreateContextAsync();

        var result = await context.Gold.UpdateQuoteAsync(
            new UpdateGoldQuoteRequest(900m, BusinessToday, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        Assert.NotNull(context.Repository.GoldHolding);
        Assert.Equal(0m, context.Repository.GoldHolding.Grams);
        Assert.Equal(0m, context.Repository.GoldHolding.Value);
    }

    [Fact]
    public async Task SaveHoldingAsync_WithZeroGramsAndNonZeroCost_ReturnsInvalid()
    {
        var context = await CreateContextAsync();

        var result = await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(0m, 100m, 900m, BusinessToday, null, Guid.NewGuid(), HoldingDate: BusinessToday),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Null(context.Repository.GoldHolding);
    }

    [Fact]
    public async Task CashAndHomeOverview_ReflectGoldBuyAndSell()
    {
        var context = await CreateContextAsync();
        await SaveExistingHoldingAsync(context);
        await context.Gold.BuyAsync(
            new BuyGoldRequest(1m, 900m, 910m, BusinessToday, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Gold.SellAsync(
            new SellGoldRequest(1m, 898m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var cash = await context.Cash.GetOverviewAsync(CancellationToken.None);
        var home = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(cash);
        Assert.Equal(9998m, cash.Balance);
        Assert.Equal(898m, cash.Inflow);
        Assert.Equal(900m, cash.Outflow);
        Assert.Equal(-2m, cash.NetChange);
        Assert.Equal(2, cash.Activities.Count);
        Assert.All(cash.Activities, activity => Assert.Equal(CashActivityType.AssetTransfer, activity.Type));
        Assert.Contains(cash.Activities, activity => activity.Title == "积存金买入");
        Assert.Contains(cash.Activities, activity => activity.Title == "积存金卖出");
        Assert.NotNull(home);
        Assert.True(home.Gold.IsConfigured);
        Assert.Equal(27300m, home.Gold.Amount);
        Assert.Equal(37298m, home.TotalAssets);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutLedger_ReturnsNull()
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var service = new GoldService(repository, provider);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Null(overview);
    }

    private static async Task SaveExistingHoldingAsync(TestContext context)
    {
        var result = await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, BusinessToday, null, Guid.NewGuid(), HoldingDate: BusinessToday),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
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
            new GoldService(repository, provider));
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        LedgerService Ledger,
        CashService Cash,
        GoldService Gold);
}
