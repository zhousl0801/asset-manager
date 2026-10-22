using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Assets;

public sealed class ProvidentFundServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    [Fact]
    public async Task DepositAsync_CreatesAccountWithoutChangingCash()
    {
        var context = await CreateContextAsync();

        var result = await context.Provident.DepositAsync(
            new ProvidentDepositRequest(5000m, BusinessToday, "个人＋单位", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(5000m, result.Result.Balance);
        Assert.Equal(ProvidentActivityType.Deposit, result.Result.Activity.Type);
        Assert.Equal(5000m, result.Result.Activity.BalanceAfter);
        Assert.Equal(1000m, context.Repository.CashAccount!.Balance);
        Assert.NotNull(context.Repository.ProvidentAccount);
        Assert.Equal(5000m, context.Repository.ProvidentAccount.Balance);
        Assert.Single(context.Repository.AssetTransactions);
    }

    [Fact]
    public async Task DepositAsync_WithDuplicateRequestId_ReturnsExistingAndDoesNotDoubleCount()
    {
        var context = await CreateContextAsync();
        var requestId = Guid.NewGuid();
        var request = new ProvidentDepositRequest(5000m, BusinessToday, null, requestId);

        var first = await context.Provident.DepositAsync(request, CancellationToken.None);
        var second = await context.Provident.DepositAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, first.Status);
        Assert.Equal(AssetOperationStatus.Success, second.Status);
        Assert.NotNull(second.Result);
        Assert.Equal(5000m, second.Result.Balance);
        Assert.Equal(5000m, context.Repository.ProvidentAccount!.Balance);
        Assert.Single(context.Repository.AssetTransactions);
    }

    [Fact]
    public async Task WithdrawAsync_MovesEqualAmountToCash()
    {
        var context = await CreateContextAsync();
        await context.Provident.DepositAsync(
            new ProvidentDepositRequest(5000m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Provident.WithdrawAsync(
            new ProvidentWithdrawalRequest(2000m, BusinessToday, "提取到现金", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal(3000m, result.Result.Balance);
        Assert.Equal(ProvidentActivityType.Withdrawal, result.Result.Activity.Type);
        Assert.Equal(3000m, context.Repository.CashAccount!.Balance);
        Assert.Equal(2, context.Repository.AssetTransactions.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsMonthFlowAndRunningBalance()
    {
        var context = await CreateContextAsync();
        await context.Provident.DepositAsync(
            new ProvidentDepositRequest(5000m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Provident.WithdrawAsync(
            new ProvidentWithdrawalRequest(2000m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var overview = await context.Provident.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.True(overview.IsConfigured);
        Assert.Equal(3000m, overview.Balance);
        Assert.Equal(5000m, overview.MonthDeposit);
        Assert.Equal(2000m, overview.MonthWithdrawal);
        Assert.Equal(3000m, overview.NetChange);
        Assert.Equal(2, overview.Activities.Count);
        var withdrawal = overview.Activities.Single(activity => activity.Type == ProvidentActivityType.Withdrawal);
        var deposit = overview.Activities.Single(activity => activity.Type == ProvidentActivityType.Deposit);
        Assert.Equal(3000m, withdrawal.BalanceAfter);
        Assert.Equal(5000m, deposit.BalanceAfter);
    }

    [Fact]
    public async Task WithdrawAsync_WithInsufficientBalance_ReturnsInvalid()
    {
        var context = await CreateContextAsync();
        await context.Provident.DepositAsync(
            new ProvidentDepositRequest(100m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        var result = await context.Provident.WithdrawAsync(
            new ProvidentWithdrawalRequest(100.01m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("公积金余额不足"));
        Assert.Equal(100m, context.Repository.ProvidentAccount!.Balance);
        Assert.Equal(1000m, context.Repository.CashAccount!.Balance);
    }

    [Fact]
    public async Task DepositAsync_WithDateBeforeLedgerStart_ReturnsInvalid()
    {
        var context = await CreateContextAsync();

        var result = await context.Provident.DepositAsync(
            new ProvidentDepositRequest(100m, BusinessToday.AddDays(-1), null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Contains("业务日期不能早于账本起始日期。", result.Errors);
        Assert.Null(context.Repository.ProvidentAccount);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutLedger_ReturnsNull()
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var service = new ProvidentFundService(repository, provider);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Null(overview);
    }

    [Fact]
    public async Task CashAndHomeOverview_ReflectProvidentWithdrawal()
    {
        var context = await CreateContextAsync();
        await context.Provident.DepositAsync(
            new ProvidentDepositRequest(5000m, BusinessToday, null, Guid.NewGuid()),
            CancellationToken.None);
        await context.Provident.WithdrawAsync(
            new ProvidentWithdrawalRequest(2000m, BusinessToday, "提取", Guid.NewGuid()),
            CancellationToken.None);

        var cash = await context.Cash.GetOverviewAsync(CancellationToken.None);
        var home = await context.Ledger.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(cash);
        Assert.Equal(3000m, cash.Balance);
        Assert.Equal(2000m, cash.Inflow);
        Assert.Equal(2000m, cash.NetChange);
        var transfer = Assert.Single(cash.Activities);
        Assert.Equal(CashActivityType.AssetTransfer, transfer.Type);
        Assert.Equal("公积金提取", transfer.Title);
        Assert.Equal(AssetType.ProvidentFund, transfer.AssetType);
        Assert.NotNull(home);
        Assert.True(home.ProvidentFund.IsConfigured);
        Assert.Equal(3000m, home.ProvidentFund.Amount);
        Assert.Equal(6000m, home.TotalAssets);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var repository = new FakeLedgerRepository();
        var provider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, provider);
        await ledgerService.InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, 1000m),
            CancellationToken.None);

        return new TestContext(
            repository,
            ledgerService,
            new CashService(repository, provider),
            new ProvidentFundService(repository, provider));
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        LedgerService Ledger,
        CashService Cash,
        ProvidentFundService Provident);
}
