using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Funds;

namespace AssetKeeper.Tests.Api;

public sealed class FundReturnEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task ReturnsEndpoint_ReturnsIntervalAndHoldingSeparately()
    {
        using var factory = new AssetKeeperApiFactory();
        factory.Quotes.Date = new DateOnly(2026, 9, 20);
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-09-01",
            cashBalance = 10000m
        });
        var position = await CreatePositionAsync(client);
        await UpdateQuoteAsync(factory, client, position, 11m, "2026-09-30");
        await UpdateQuoteAsync(factory, client, position, 12m, "2026-10-10");

        var response = await client.GetAsync("/api/ledger/funds/returns?range=month&anchor=2026-10-21");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FundReturnDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(100m, result.IntervalProfit);
        Assert.Equal(200m, result.HoldingProfit);
        Assert.Equal(100m, result.Points.Single(point => point.Date == new DateOnly(2026, 10, 10)).DailyProfit);
        Assert.Null(result.Points.Single(point => point.Date == new DateOnly(2026, 10, 5)).DailyProfit);

        var day = await client.GetFromJsonAsync<FundReturnDayDto>(
            "/api/ledger/funds/returns/day?date=2026-10-10",
            JsonOptions);
        Assert.NotNull(day);
        Assert.Equal(100m, day.TotalProfit);
        Assert.True(day.IsComplete);
    }

    [Fact]
    public async Task DayEndpoint_RequiresDate()
    {
        using var factory = new AssetKeeperApiFactory();
        factory.Quotes.Date = new DateOnly(2026, 9, 20);
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-09-01",
            cashBalance = 10000m
        });

        var response = await client.GetAsync("/api/ledger/funds/returns/day");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> CreatePositionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/ledger/funds/positions", new
        {
            name = "纳指基金",
            code = "000001",
            shareClass = "A",
            platform = "平台一",
            shares = 100m,
            cost = 1000m,
            netValue = 10m,
            netValueDate = "2026-09-20",
            holdingDate = "2026-09-20",
            note = (string?)null,
            requestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);

        return result!.Position.Id;
    }

    private static async Task UpdateQuoteAsync(AssetKeeperApiFactory factory, HttpClient client, Guid positionId, decimal netValue, string date)
    {
        factory.Quotes.NetValue = netValue;
        factory.Quotes.Date = DateOnly.Parse(date);
        var response = await client.PostAsync($"/api/ledger/funds/{positionId}/quote/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
