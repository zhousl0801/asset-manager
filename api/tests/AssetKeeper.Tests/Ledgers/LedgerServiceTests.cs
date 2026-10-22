using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Tests.Ledgers;

public sealed class LedgerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    [Fact]
    public async Task InitializeAsync_WithValidRequest_CreatesLedgerAndCashAccount()
    {
        var repository = new FakeLedgerRepository();
        var service = CreateService(repository);

        var result = await service.InitializeAsync(
            new InitializeLedgerRequest(new DateOnly(2026, 10, 1), 23400.00m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Created, result.Status);
        Assert.NotNull(result.Overview);
        Assert.Equal(23400.00m, result.Overview.TotalAssets);
        Assert.True(result.Overview.Cash.IsConfigured);
        Assert.Equal(23400.00m, result.Overview.Cash.Amount!.Value);
        Assert.False(result.Overview.Fund.IsConfigured);
        Assert.False(result.Overview.ProvidentFund.IsConfigured);
        Assert.False(result.Overview.Gold.IsConfigured);
        Assert.False(result.Overview.Month.HasRecords);
        Assert.Equal(new DateOnly(2026, 10, 1), result.Overview.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 1), result.Overview.AsOfDate);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task InitializeAsync_WithZeroBalance_CreatesLedger()
    {
        var result = await CreateService(new FakeLedgerRepository()).InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, 0m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Created, result.Status);
        Assert.NotNull(result.Overview);
        Assert.Equal(0m, result.Overview.TotalAssets);
    }

    [Fact]
    public async Task InitializeAsync_WithFutureStartDate_ReturnsInvalid()
    {
        var result = await CreateService(new FakeLedgerRepository()).InitializeAsync(
            new InitializeLedgerRequest(BusinessToday.AddDays(1), 100m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Invalid, result.Status);
        Assert.Contains("起始日期不能晚于今天。", result.Errors);
    }

    [Fact]
    public async Task InitializeAsync_WithNegativeBalance_ReturnsInvalid()
    {
        var result = await CreateService(new FakeLedgerRepository()).InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, -0.01m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Invalid, result.Status);
        Assert.Contains("现金余额不能为负数。", result.Errors);
    }

    [Fact]
    public async Task InitializeAsync_WithMoreThanTwoDecimals_ReturnsInvalid()
    {
        var result = await CreateService(new FakeLedgerRepository()).InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, 10.001m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Invalid, result.Status);
        Assert.Contains("现金余额最多保留两位小数。", result.Errors);
    }

    [Fact]
    public async Task InitializeAsync_WithBalanceAboveMaximum_ReturnsInvalid()
    {
        var result = await CreateService(new FakeLedgerRepository()).InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, CashAccount.MaximumBalance + 0.01m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Invalid, result.Status);
        Assert.Contains("现金余额超出允许范围。", result.Errors);
    }

    [Fact]
    public async Task InitializeAsync_WhenLedgerAlreadyExists_ReturnsAlreadyExists()
    {
        var repository = new FakeLedgerRepository();
        var service = CreateService(repository);
        var request = new InitializeLedgerRequest(BusinessToday, 100m);

        await service.InitializeAsync(request, CancellationToken.None);
        var result = await service.InitializeAsync(request, CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.AlreadyExists, result.Status);
        Assert.Null(result.Overview);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task GetOverviewAsync_WithoutLedger_ReturnsNull()
    {
        var overview = await CreateService(new FakeLedgerRepository()).GetOverviewAsync(CancellationToken.None);

        Assert.Null(overview);
    }

    [Fact]
    public async Task GetOverviewAsync_AfterInitialization_ReturnsCashBalance()
    {
        var repository = new FakeLedgerRepository();
        var service = CreateService(repository);

        await service.InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, 5200.50m),
            CancellationToken.None);
        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(5200.50m, overview.TotalAssets);
        Assert.Equal(5200.50m, overview.Cash.Amount!.Value);
    }

    [Fact]
    public async Task InitializeAsync_AfterShanghaiMidnight_AcceptsNewBusinessDate()
    {
        var now = new DateTimeOffset(2026, 10, 21, 16, 30, 0, TimeSpan.Zero);
        var service = CreateService(new FakeLedgerRepository(), now);

        var result = await service.InitializeAsync(
            new InitializeLedgerRequest(new DateOnly(2026, 10, 22), 100m),
            CancellationToken.None);

        Assert.Equal(InitializeLedgerStatus.Created, result.Status);
    }

    [Fact]
    public async Task GetOverviewAsync_WithCurrentMonthEntries_ReturnsMonthSummary()
    {
        var repository = new FakeLedgerRepository();
        var businessDateProvider = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, businessDateProvider);
        var entryService = new LedgerEntryService(repository, businessDateProvider);

        await ledgerService.InitializeAsync(
            new InitializeLedgerRequest(BusinessToday, 1000m),
            CancellationToken.None);
        await entryService.CreateAsync(
            new CreateLedgerEntryRequest(
                LedgerEntryKind.Expense,
                "餐饮",
                35.60m,
                BusinessToday,
                "午餐",
                Guid.NewGuid()),
            CancellationToken.None);
        await entryService.CreateAsync(
            new CreateLedgerEntryRequest(
                LedgerEntryKind.Income,
                "工资",
                12000m,
                BusinessToday,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        var overview = await ledgerService.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal(12000m, overview.Month.Income);
        Assert.Equal(35.60m, overview.Month.Expense);
        Assert.Equal(11964.40m, overview.Month.Surplus);
        Assert.True(overview.Month.HasRecords);
        Assert.Equal(12964.40m, overview.TotalAssets);
    }

    private static LedgerService CreateService(FakeLedgerRepository repository)
    {
        return CreateService(repository, Now);
    }

    private static LedgerService CreateService(FakeLedgerRepository repository, DateTimeOffset now)
    {
        return new LedgerService(repository, new BusinessDateProvider(new FixedTimeProvider(now)));
    }
}
