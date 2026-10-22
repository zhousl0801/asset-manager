using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class CashEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task GetCash_BeforeInitialization_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ledger/cash");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Initialize_ThenGetCash_ReturnsBalanceAndMonthWindow()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var today = BusinessToday();

        var response = await client.GetAsync("/api/ledger/cash");
        var overview = await response.Content.ReadFromJsonAsync<CashOverviewDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(overview);
        Assert.Equal(1000m, overview.Balance);
        Assert.Equal(today, overview.AsOfDate);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), overview.From);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(0m, overview.Inflow);
        Assert.Equal(0m, overview.Outflow);
        Assert.Equal(0m, overview.NetChange);
        Assert.Empty(overview.Activities);
    }

    [Fact]
    public async Task CreateEntries_ThenGetCash_ReturnsFlowSummaryAndActivities()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var today = BusinessToday();

        await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "income",
            category = "工资",
            amount = 500m,
            businessDate = today.ToString("yyyy-MM-dd"),
            note = (string?)null,
            requestId = Guid.NewGuid()
        });
        await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "expense",
            category = "餐饮",
            amount = 35.60m,
            businessDate = today.ToString("yyyy-MM-dd"),
            note = "午餐",
            requestId = Guid.NewGuid()
        });

        var response = await client.GetAsync("/api/ledger/cash");
        var rawJson = await response.Content.ReadAsStringAsync();
        var overview = await response.Content.ReadFromJsonAsync<CashOverviewDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"type\":\"daily\"", rawJson);
        Assert.NotNull(overview);
        Assert.Equal(1464.40m, overview.Balance);
        Assert.Equal(1000m, overview.OpeningBalance);
        Assert.Equal(500m, overview.Inflow);
        Assert.Equal(35.60m, overview.Outflow);
        Assert.Equal(0m, overview.AdjustmentTotal);
        Assert.Equal(464.40m, overview.NetChange);
        Assert.Equal(2, overview.Activities.Count);
    }

    [Fact]
    public async Task Reconcile_WithDifference_CreatesAdjustmentAndKeepsLivingStatsUnchanged()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var today = BusinessToday();

        var response = await client.PostAsJsonAsync("/api/ledger/cash/reconcile", new
        {
            requestId = Guid.NewGuid(),
            expectedVersion = 0,
            actualBalance = 950m,
            reason = "漏记支出"
        });
        var result = await response.Content.ReadFromJsonAsync<CashReconcileResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.False(result.IsBalanced);
        Assert.Equal(-50m, result.Difference);
        Assert.Equal(950m, result.CashBalance);
        Assert.NotNull(result.Adjustment);
        Assert.Equal(1000m, result.Adjustment.PreviousBalance);
        Assert.Equal(950m, result.Adjustment.NewBalance);
        Assert.Equal(-50m, result.Adjustment.Difference);
        Assert.Equal("漏记支出", result.Adjustment.Reason);
        Assert.Equal(today, result.Adjustment.BusinessDate);

        var cashResponse = await client.GetAsync("/api/ledger/cash");
        var cashJson = await cashResponse.Content.ReadAsStringAsync();
        var cash = await cashResponse.Content.ReadFromJsonAsync<CashOverviewDto>(JsonOptions);

        Assert.Contains("\"type\":\"adjustment\"", cashJson);
        Assert.NotNull(cash);
        Assert.Equal(950m, cash.Balance);
        Assert.Equal(-50m, cash.AdjustmentTotal);
        Assert.Equal(-50m, cash.NetChange);
        var activity = Assert.Single(cash.Activities);
        Assert.Equal(CashActivityType.Adjustment, activity.Type);
        Assert.Equal("余额调整", activity.Title);
        Assert.Equal(-50m, activity.Amount);

        var ledgerResponse = await client.GetAsync("/api/ledger");
        var ledger = await ledgerResponse.Content.ReadFromJsonAsync<LedgerOverviewDto>(JsonOptions);

        Assert.NotNull(ledger);
        Assert.Equal(950m, ledger.TotalAssets);
        Assert.False(ledger.Month.HasRecords);
        Assert.Equal(0m, ledger.Month.Income);
        Assert.Equal(0m, ledger.Month.Expense);

        var expenseResponse = await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "expense",
            category = "购物",
            amount = 100m,
            businessDate = today.ToString("yyyy-MM-dd"),
            note = (string?)null,
            requestId = Guid.NewGuid()
        });
        var expense = await expenseResponse.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, expenseResponse.StatusCode);
        Assert.NotNull(expense);
        Assert.Equal(850m, expense.CashBalance);
    }

    [Fact]
    public async Task Reconcile_WithEqualBalance_ReturnsBalancedWithoutAdjustment()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);

        var response = await client.PostAsJsonAsync("/api/ledger/cash/reconcile", new
        {
            requestId = Guid.NewGuid(),
            expectedVersion = 0,
            actualBalance = 1000m,
            reason = ""
        });
        var result = await response.Content.ReadFromJsonAsync<CashReconcileResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.IsBalanced);
        Assert.Equal(0m, result.Difference);
        Assert.Equal(1000m, result.CashBalance);
        Assert.Null(result.Adjustment);

        var cash = await client.GetFromJsonAsync<CashOverviewDto>("/api/ledger/cash", JsonOptions);

        Assert.NotNull(cash);
        Assert.Empty(cash.Activities);
        Assert.Equal(1000m, cash.Balance);
    }

    [Fact]
    public async Task Reconcile_WithDifferenceAndBlankReason_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);

        var response = await client.PostAsJsonAsync("/api/ledger/cash/reconcile", new
        {
            requestId = Guid.NewGuid(),
            expectedVersion = 0,
            actualBalance = 950m,
            reason = "  "
        });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("调整原因不能为空。", problem.Errors.SelectMany(error => error.Value));
    }

    [Fact]
    public async Task Reconcile_WithNegativeBalance_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);

        var response = await client.PostAsJsonAsync("/api/ledger/cash/reconcile", new
        {
            requestId = Guid.NewGuid(),
            expectedVersion = 0,
            actualBalance = -0.01m,
            reason = "核对"
        });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("核对后的现金余额不能为负数。", problem.Errors.SelectMany(error => error.Value));
    }

    [Fact]
    public async Task Reconcile_WithoutLedger_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ledger/cash/reconcile", new
        {
            requestId = Guid.NewGuid(),
            expectedVersion = 0,
            actualBalance = 100m,
            reason = "核对"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task InitializeLedgerAsync(HttpClient client, decimal cashBalance)
    {
        var today = BusinessToday();
        var response = await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = today.ToString("yyyy-MM-dd"),
            cashBalance
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static DateOnly BusinessToday()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record ValidationProblemDetailsDto(Dictionary<string, string[]> Errors);
}
