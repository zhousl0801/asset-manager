using System.Net;
using System.Net.Http.Json;
using AssetKeeper.Application.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class LedgerEndpointsTests
{
    [Fact]
    public async Task GetLedger_BeforeInitialization_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ledger");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Initialize_ThenGet_ReturnsCashOverview()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var initializeResponse = await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 23400.00m
        });

        Assert.Equal(HttpStatusCode.Created, initializeResponse.StatusCode);
        Assert.Equal("/api/ledger", initializeResponse.Headers.Location?.PathAndQuery);

        var created = await initializeResponse.Content.ReadFromJsonAsync<LedgerOverviewDto>();
        Assert.NotNull(created);
        Assert.Equal(23400.00m, created.TotalAssets);
        Assert.True(created.Cash.IsConfigured);
        Assert.False(created.Fund.IsConfigured);

        var getResponse = await client.GetAsync("/api/ledger");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var rawJson = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"totalAssets\"", rawJson);
        Assert.Contains("\"isConfigured\"", rawJson);
        Assert.Contains("\"providentFund\"", rawJson);
        Assert.Contains("\"hasRecords\"", rawJson);

        var overview = await getResponse.Content.ReadFromJsonAsync<LedgerOverviewDto>();
        Assert.NotNull(overview);
        Assert.Equal(23400.00m, overview.Cash.Amount!.Value);
        Assert.Equal(new DateOnly(2026, 10, 1), overview.StartDate);
        Assert.Equal("CNY", overview.Currency);
        Assert.Equal("Asia/Shanghai", overview.TimeZone);
    }

    [Fact]
    public async Task Initialize_WhenLedgerExists_ReturnsConflict()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        var request = new
        {
            startDate = "2026-10-01",
            cashBalance = 100m
        };

        await client.PostAsJsonAsync("/api/ledger/initialize", request);
        var secondResponse = await client.PostAsJsonAsync("/api/ledger/initialize", request);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Initialize_WithFutureDate_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2099-01-01",
            cashBalance = 100m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();
        Assert.NotNull(problem);
        Assert.Contains("起始日期不能晚于今天。", problem.Errors.SelectMany(error => error.Value));
    }

    [Fact]
    public async Task Initialize_WithNegativeBalance_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = -0.01m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();
        Assert.NotNull(problem);
        Assert.Contains("现金余额不能为负数。", problem.Errors.SelectMany(error => error.Value));
    }

    private sealed record ValidationProblemDetailsDto(Dictionary<string, string[]> Errors);
}
