using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Tests.Api;

public sealed class QuoteEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task StatusAndBatchRefresh_ReturnMonitorStateWithoutHoldings()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 10000m
        });

        var statusResponse = await client.GetAsync("/api/ledger/quotes/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<QuoteStatusDto>(JsonOptions);
        Assert.NotNull(status);
        Assert.False(status.SchedulerEnabled);
        Assert.False(status.IsRunning);
        Assert.Contains(status.Items, item => item.AssetType == "gold" && item.Status == "notRecorded");

        var refreshResponse = await client.PostAsJsonAsync("/api/ledger/quotes/refresh", new { scope = "all" });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var run = await refreshResponse.Content.ReadFromJsonAsync<QuoteRefreshRunDto>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal("manual", run.Trigger);
        Assert.Equal(0, run.FundAttempted);
        Assert.Equal(0, run.GoldAttempted);

        var after = await client.GetFromJsonAsync<QuoteStatusDto>("/api/ledger/quotes/status", JsonOptions);
        Assert.NotNull(after);
        Assert.Equal("manual", after.LastRun?.Trigger);
    }

    [Fact]
    public async Task RefreshWithoutLedger_ReturnsNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ledger/quotes/refresh", new { scope = "all" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
