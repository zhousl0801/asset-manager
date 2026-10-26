using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Tests.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class AssetEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task AssetEndpoints_BeforeInitialization_ReturnNotFound()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();

        var provident = await client.GetAsync("/api/ledger/provident");
        var gold = await client.GetAsync("/api/ledger/gold");
        var funds = await client.GetAsync("/api/ledger/funds");

        Assert.Equal(HttpStatusCode.NotFound, provident.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gold.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, funds.StatusCode);
    }

    [Fact]
    public async Task AssetFlows_UpdateCashHomeAndCashActivityStream()
    {
        using var factory = new AssetKeeperApiFactory
        {
            Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero))
        };
        using var client = factory.CreateClient();
        var today = new DateOnly(2026, 10, 26);
        factory.Quotes.NetValue = 12m;
        factory.Quotes.Date = today.AddDays(-7);
        var date = today.ToString("yyyy-MM-dd");
        await InitializeLedgerAsync(client, 10000m, today.AddDays(-7));

        var depositResponse = await client.PostAsJsonAsync("/api/ledger/provident/deposits", new
        {
            amount = 5000m,
            businessDate = date,
            note = "个人＋单位",
            requestId = Guid.NewGuid()
        });
        var withdrawalResponse = await client.PostAsJsonAsync("/api/ledger/provident/withdrawals", new
        {
            amount = 2000m,
            businessDate = date,
            note = "提取",
            requestId = Guid.NewGuid()
        });
        var holdingResponse = await client.PostAsJsonAsync("/api/ledger/gold/holding", new
        {
            grams = 30m,
            cost = 25600m,
            quote = 900m,
            quoteDate = date,
            holdingDate = date,
            note = "已有持仓",
            requestId = Guid.NewGuid()
        });
        var goldBuyResponse = await client.PostAsJsonAsync("/api/ledger/gold/buys", new
        {
            grams = 1m,
            amount = 900m,
            quote = 910m,
            quoteDate = date,
            holdingDate = date,
            businessDate = date,
            note = "买入",
            requestId = Guid.NewGuid()
        });
        var goldSellResponse = await client.PostAsJsonAsync("/api/ledger/gold/sells", new
        {
            grams = 1m,
            netAmount = 898m,
            businessDate = date,
            note = "卖出",
            requestId = Guid.NewGuid()
        });
        var positionResponse = await client.PostAsJsonAsync("/api/ledger/funds/positions", new
        {
            name = "纳指基金",
            code = "000001",
            shareClass = "A",
            platform = "示例平台",
            shares = 100m,
            cost = 1000m,
            netValue = 12m,
            netValueDate = date,
            holdingDate = today.AddDays(-7).ToString("yyyy-MM-dd"),
            note = "已有持仓",
            requestId = Guid.NewGuid()
        });
        var position = await positionResponse.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);
        Assert.NotNull(position);
        var fundBuyResponse = await client.PostAsJsonAsync("/api/ledger/funds/buys", new
        {
            positionId = position.Position.Id,
            amount = 130m,
            businessDate = today.AddDays(-7).ToString("yyyy-MM-dd"),
            note = "加仓",
            requestId = Guid.NewGuid()
        });
        var fundBuy = await fundBuyResponse.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);
        Assert.NotNull(fundBuy);
        factory.Quotes.NetValue = 13m;
        var fundConfirmResponse = await client.PostAsync($"/api/ledger/funds/{position.Position.Id}/quote/refresh", null);
        var fundSellApplyResponse = await client.PostAsJsonAsync("/api/ledger/funds/sells", new
        {
            positionId = position.Position.Id,
            shares = 10m,
            businessDate = date,
            note = "赎回",
            requestId = Guid.NewGuid()
        });
        var fundSellApply = await fundSellApplyResponse.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);
        Assert.NotNull(fundSellApply);
        var fundSellConfirmResponse = await client.PostAsJsonAsync($"/api/ledger/funds/sells/{fundSellApply.Activity.Id}/confirm", new
        {
            shares = 10m,
            netValue = 13m,
            netValueDate = date,
            fee = 0m,
            netAmount = 150m,
            businessDate = date,
            note = "确认",
            requestId = Guid.NewGuid()
        });
        var fundSellConfirm = await fundSellConfirmResponse.Content.ReadFromJsonAsync<FundMutationResultDto>(JsonOptions);
        Assert.NotNull(fundSellConfirm);
        var fundSellArrivalResponse = await client.PostAsJsonAsync($"/api/ledger/funds/sells/{fundSellConfirm.Activity.Id}/arrival", new
        {
            amount = 150m,
            businessDate = date,
            note = "到账",
            requestId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.OK, depositResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withdrawalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, holdingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, goldBuyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, goldSellResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, positionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fundBuyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fundConfirmResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fundSellApplyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fundSellConfirmResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fundSellArrivalResponse.StatusCode);

        var provident = await client.GetFromJsonAsync<ProvidentOverviewDto>(
            "/api/ledger/provident",
            JsonOptions);
        var gold = await client.GetFromJsonAsync<GoldOverviewDto>("/api/ledger/gold", JsonOptions);
        var funds = await client.GetFromJsonAsync<FundOverviewDto>("/api/ledger/funds", JsonOptions);
        var cash = await client.GetFromJsonAsync<CashOverviewDto>("/api/ledger/cash", JsonOptions);
        var home = await client.GetFromJsonAsync<LedgerOverviewDto>("/api/ledger", JsonOptions);

        Assert.NotNull(provident);
        Assert.Equal(3000m, provident.Balance);
        Assert.Equal(5000m, provident.MonthDeposit);
        Assert.Equal(2000m, provident.MonthWithdrawal);
        Assert.Equal(2, provident.Activities.Count);

        Assert.NotNull(gold);
        Assert.Equal(30m, gold.Grams);
        Assert.Equal(25645.16m, gold.Cost);
        Assert.Equal(27300m, gold.Value);
        Assert.Equal(43.16m, gold.RealizedProfit);

        Assert.NotNull(funds);
        Assert.Equal(1300m, funds.TotalValue);
        Assert.Single(funds.Positions);
        Assert.Equal(7, funds.Activities.Count);

        Assert.NotNull(cash);
        Assert.Equal(12018m, cash.Balance);
        Assert.Equal(3048m, cash.Inflow);
        Assert.Equal(1030m, cash.Outflow);
        Assert.Equal(2018m, cash.NetChange);
        Assert.Equal(5, cash.Activities.Count);
        Assert.All(cash.Activities, activity => Assert.Equal(CashActivityType.AssetTransfer, activity.Type));
        Assert.Contains(cash.Activities, activity => activity.Title == "公积金提取");
        Assert.Contains(cash.Activities, activity => activity.Title == "基金买入待确认");
        Assert.Contains(cash.Activities, activity => activity.Title == "基金赎回到账");
        Assert.Contains(cash.Activities, activity => activity.Title == "积存金卖出");

        Assert.NotNull(home);
        Assert.Equal(43618m, home.TotalAssets);
        Assert.True(home.ProvidentFund.IsConfigured);
        Assert.Equal(3000m, home.ProvidentFund.Amount);
        Assert.True(home.Fund.IsConfigured);
        Assert.Equal(1300m, home.Fund.Amount);
        Assert.True(home.Gold.IsConfigured);
        Assert.Equal(27300m, home.Gold.Amount);
        Assert.False(home.Month.HasRecords);
    }

    [Fact]
    public async Task WithdrawMoreThanProvidentBalance_ReturnsValidationProblem()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        var date = BusinessToday().ToString("yyyy-MM-dd");
        await InitializeLedgerAsync(client, 1000m);

        var response = await client.PostAsJsonAsync("/api/ledger/provident/withdrawals", new
        {
            amount = 1m,
            businessDate = date,
            note = (string?)null,
            requestId = Guid.NewGuid()
        });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("公积金余额不足。", problem.Errors.SelectMany(error => error.Value));
    }

    private static async Task InitializeLedgerAsync(HttpClient client, decimal cashBalance, DateOnly? startDate = null)
    {
        var today = BusinessToday();
        var response = await client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = (startDate ?? today).ToString("yyyy-MM-dd"),
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
