using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class LedgerEntriesEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CreateExpense_ThenGetPeriod_ReturnsSummaryAndEntry()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var today = BusinessToday();

        var createResponse = await client.PostAsJsonAsync("/api/ledger/entries", Expense(35.60m));
        var createJson = await createResponse.Content.ReadAsStringAsync();
        var created = await createResponse.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Contains("\"kind\":\"expense\"", createJson);
        Assert.Contains("\"status\":\"active\"", createJson);
        Assert.NotNull(created);
        Assert.Equal(964.40m, created.CashBalance);
        Assert.Equal(LedgerEntryKind.Expense, created.Entry.Kind);

        var periodResponse = await client.GetAsync(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}");
        var period = await periodResponse.Content.ReadFromJsonAsync<EntryPeriodDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, periodResponse.StatusCode);
        Assert.NotNull(period);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), period.From);
        Assert.Equal(35.60m, period.Expense);
        Assert.Single(period.Entries);
        Assert.Equal("餐饮", period.Entries[0].Category);

        var expensePeriod = await client.GetFromJsonAsync<EntryPeriodDto>(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}&kind=expense",
            JsonOptions);
        var incomePeriod = await client.GetFromJsonAsync<EntryPeriodDto>(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}&kind=income",
            JsonOptions);
        var categoryPeriod = await client.GetFromJsonAsync<EntryPeriodDto>(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}&category={Uri.EscapeDataString("餐饮")}",
            JsonOptions);
        var searchPeriod = await client.GetFromJsonAsync<EntryPeriodDto>(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}&search={Uri.EscapeDataString("午餐")}",
            JsonOptions);

        Assert.NotNull(expensePeriod);
        Assert.Single(expensePeriod.Entries);
        Assert.NotNull(incomePeriod);
        Assert.Empty(incomePeriod.Entries);
        Assert.NotNull(categoryPeriod);
        Assert.Single(categoryPeriod.Entries);
        Assert.NotNull(searchPeriod);
        Assert.Single(searchPeriod.Entries);

        var overview = await client.GetFromJsonAsync<LedgerOverviewDto>("/api/ledger");

        Assert.NotNull(overview);
        Assert.Equal(35.60m, overview.Month.Expense);
        Assert.True(overview.Month.HasRecords);
        Assert.Equal(964.40m, overview.TotalAssets);
    }

    [Fact]
    public async Task CreateWithSameRequestId_ReturnsExistingEntry()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var requestId = Guid.NewGuid();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/ledger/entries",
            Expense(100m, requestId: requestId));
        var secondResponse = await client.PostAsJsonAsync(
            "/api/ledger/entries",
            Expense(100m, requestId: requestId));

        var first = await firstResponse.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);
        var second = await secondResponse.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Entry.Id, second.Entry.Id);
        Assert.Equal(900m, second.CashBalance);
    }

    [Fact]
    public async Task UpdateEntry_AdjustsCashByDifference()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var created = await CreateEntryAsync(client, Expense(100m));
        var today = BusinessToday();

        var response = await client.PutAsJsonAsync(
            $"/api/ledger/entries/{created.Entry.Id}",
            new
            {
                requestId = Guid.NewGuid(),
                kind = "expense",
                category = "餐饮",
                amount = 150m,
                businessDate = today.ToString("yyyy-MM-dd"),
                note = "晚餐"
            });
        var result = await response.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(850m, result.CashBalance);
        Assert.Equal(150m, result.Entry.Amount);
        Assert.Equal("晚餐", result.Entry.Note);
    }

    [Fact]
    public async Task ReverseEntry_RestoresCashAndHidesEntryFromPeriod()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);
        var created = await CreateEntryAsync(client, Expense(100m));
        var today = BusinessToday();

        var response = await client.PostAsJsonAsync(
            $"/api/ledger/entries/{created.Entry.Id}/reverse",
            new { reason = "重复记录", requestId = Guid.NewGuid() });
        var result = await response.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);
        var period = await client.GetFromJsonAsync<EntryPeriodDto>(
            $"/api/ledger/entries?period=month&anchor={today:yyyy-MM-dd}",
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1000m, result.CashBalance);
        Assert.Equal(LedgerEntryStatus.Reversed, result.Entry.Status);
        Assert.Equal("重复记录", result.Entry.ReversalReason);
        Assert.NotNull(period);
        Assert.Empty(period.Entries);
        Assert.Equal(0m, period.Expense);
    }

    [Fact]
    public async Task CreateExpenseAboveCash_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 50m);

        var response = await client.PostAsJsonAsync("/api/ledger/entries", Expense(100m));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.SelectMany(error => error.Value), error => error.Contains("现金余额不足"));
    }

    [Fact]
    public async Task CreateWithFutureDate_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeLedgerAsync(client, 1000m);

        var response = await client.PostAsJsonAsync(
            "/api/ledger/entries",
            Expense(10m, businessDate: BusinessToday().AddDays(1)));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("业务日期不能晚于今天。", problem.Errors.SelectMany(error => error.Value));
    }

    [Fact]
    public async Task GetEntries_WithoutLedger_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ledger/entries");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateEntry_WithoutLedger_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ledger/entries", Expense(10m));

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

    private static async Task<EntryMutationResultDto> CreateEntryAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/ledger/entries", request);
        var result = await response.Content.ReadFromJsonAsync<EntryMutationResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result;
    }

    private static object Expense(
        decimal amount,
        Guid? requestId = null,
        DateOnly? businessDate = null,
        string? note = "午餐")
    {
        var date = businessDate ?? BusinessToday();

        return new
        {
            kind = "expense",
            category = "餐饮",
            amount,
            businessDate = date.ToString("yyyy-MM-dd"),
            note,
            requestId = requestId ?? Guid.NewGuid()
        };
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
