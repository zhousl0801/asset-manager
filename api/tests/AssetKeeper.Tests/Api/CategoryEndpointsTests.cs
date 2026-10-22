using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class CategoryEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    [Fact]
    public async Task GetCategories_AfterInitialization_ReturnsDefaults()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(client);

        var response = await client.GetAsync("/api/ledger/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<EntryCategoryDto>>(JsonOptions);
        Assert.NotNull(categories);
        Assert.Equal(18, categories.Count);
        Assert.Contains(categories, category => category.SystemKey == "salary");
    }

    [Fact]
    public async Task RenameCategory_UpdatesExistingEntries()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(client);
        var categories = await client.GetFromJsonAsync<List<EntryCategoryDto>>("/api/ledger/categories", JsonOptions);
        var dining = categories!.Single(category => category.Kind == LedgerEntryKind.Expense && category.Name == "餐饮");
        var create = await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "expense",
            category = "餐饮",
            amount = 35.60m,
            businessDate = "2026-10-01",
            note = "午餐",
            requestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var rename = await client.PutAsJsonAsync($"/api/ledger/categories/{dining.Id}", new
        {
            name = "外食",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var period = await client.GetFromJsonAsync<EntryPeriodDto>("/api/ledger/entries?period=month&anchor=2026-10-01", JsonOptions);
        Assert.NotNull(period);
        Assert.Equal("外食", Assert.Single(period.Entries).Category);
    }

    [Fact]
    public async Task GetStats_ReturnsPointsAndCategoryRanking()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(client);
        await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "expense",
            category = "购物",
            amount = 120m,
            businessDate = "2026-10-03",
            note = (string?)null,
            requestId = Guid.NewGuid()
        });

        var response = await client.GetAsync("/api/ledger/entries/stats?period=month&anchor=2026-10-03");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<EntryStatsDto>(JsonOptions);
        Assert.NotNull(stats);
        Assert.Equal(120m, stats.Expense);
        Assert.NotEmpty(stats.Points);
        var category = Assert.Single(stats.ExpenseCategories);
        Assert.Equal("购物", category.Category);
        Assert.Equal(100m, category.Share);
    }

    private static Task InitializeAsync(HttpClient client)
    {
        return client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 1000m
        });
    }
}
