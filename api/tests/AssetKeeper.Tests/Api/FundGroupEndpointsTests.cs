using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Funds;

namespace AssetKeeper.Tests.Api;

public sealed class FundGroupEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task CreateAssignAndDeleteGroup_KeepsFundAndShareClasses()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 10000m
        });
        var position = await CreatePositionAsync(client, "000001", "A", "平台一");
        await CreatePositionAsync(client, "000002", "C", "平台二");

        var createGroup = await client.PostAsJsonAsync("/api/ledger/fund-groups", new { name = "纳斯达克指数" });
        Assert.Equal(HttpStatusCode.Created, createGroup.StatusCode);
        var group = await createGroup.Content.ReadFromJsonAsync<FundGroupDto>(JsonOptions);
        Assert.NotNull(group);

        var assign = await client.PutAsJsonAsync(
            $"/api/ledger/fund-groups/positions/{position.Id}",
            new { groupId = group.Id });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assignment = await assign.Content.ReadFromJsonAsync<FundGroupAssignmentDto>(JsonOptions);
        Assert.NotNull(assignment);
        Assert.Equal(2, assignment.PositionIds.Count);

        var overview = await client.GetFromJsonAsync<FundOverviewDto>("/api/ledger/funds", JsonOptions);
        Assert.NotNull(overview);
        var fund = Assert.Single(overview.Funds ?? []);
        Assert.Equal("纳指基金", fund.Name);
        Assert.Equal(group.Id, fund.GroupId);
        Assert.Equal(2, fund.Classes.Count);
        Assert.Equal(100m, fund.Classes.Single(item => item.ShareClass == "A").Shares);
        Assert.Equal(50m, fund.Classes.Single(item => item.ShareClass == "C").Shares);
        Assert.Equal(1500m, fund.Value);
        Assert.Equal(group.Id, Assert.Single(overview.Groups ?? []).Id);

        var delete = await client.DeleteAsync($"/api/ledger/fund-groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        overview = await client.GetFromJsonAsync<FundOverviewDto>("/api/ledger/funds", JsonOptions);
        Assert.NotNull(overview);
        Assert.Empty(overview.Groups ?? []);
        Assert.All(overview.Positions, item => Assert.Null(item.GroupId));
        Assert.Equal(2, overview.Positions.Count);
    }

    [Fact]
    public async Task DuplicateGroupName_ReturnsConflict()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 10000m
        });

        await client.PostAsJsonAsync("/api/ledger/fund-groups", new { name = "美股科技" });
        var duplicate = await client.PostAsJsonAsync("/api/ledger/fund-groups", new { name = "美股科技" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task<FundPositionDto> CreatePositionAsync(
        HttpClient client,
        string code,
        string shareClass,
        string platform)
    {
        var response = await client.PostAsJsonAsync("/api/ledger/funds/positions", new
        {
            name = "纳指基金",
            code,
            shareClass,
            platform,
            shares = shareClass == "A" ? 100m : 50m,
            cost = shareClass == "A" ? 1000m : 500m,
            netValue = 10m,
            netValueDate = "2026-10-20",
            holdingDate = "2026-10-20",
            note = (string?)null,
            requestId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);

        return result!.Position;
    }
}
