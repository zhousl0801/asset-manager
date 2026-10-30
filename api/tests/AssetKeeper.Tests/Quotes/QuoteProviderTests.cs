using System.Net;
using System.Text;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Infrastructure.Quotes;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Tests.Quotes;

public sealed class QuoteProviderTests
{
    [Fact]
    public async Task EastmoneyFundQuote_ParsesLatestNavAndDate()
    {
        const string payload = """
            {"Data":{"LSJZList":[{"FSRQ":"2026-10-21","DWJZ":"1.0640"}]},"ErrCode":0}
            """;
        var provider = new EastmoneyFundQuoteProvider(CreateClient(_ => JsonResponse(payload)));

        var quote = await provider.GetLatestAsync("000001", CancellationToken.None);

        Assert.Equal("000001", quote.Code);
        Assert.Equal(1.0640m, quote.NetValue);
        Assert.Equal(new DateOnly(2026, 10, 21), quote.EffectiveDate);
        Assert.Equal("eastmoney", quote.Source);
    }

    [Fact]
    public async Task EastmoneyFundQuote_WithEmptyList_ThrowsUnavailable()
    {
        const string payload = """{"Data":{"LSJZList":[]},"ErrCode":0}""";
        var provider = new EastmoneyFundQuoteProvider(CreateClient(_ => JsonResponse(payload)));

        await Assert.ThrowsAsync<QuoteUnavailableException>(() => provider.GetLatestAsync("000001", CancellationToken.None));
    }

    [Fact]
    public async Task IcbcGoldQuote_ParsesAccumulationGoldActivePrice()
    {
        const string payload = """
            {"sysdate":"2026-10-26 10:26:51","TranErrorCode":"","rf":[{"goldTypeNo":"JC001","RegPrice":"857.92","Active":"0","ProductName":"积存金","proCode":"080020000521","Reg":"2","HighPrice":"859.44","RegDate":"2026-10-26","ActivePrice":"858.01","SellPrice":"858.01","ActiveDate":"2026-10-26","LowPrice":"855.55"}],"TranErrorDisplayMsg":""}
            """;
        var provider = new IcbcGoldQuoteProvider(CreateClient(_ => JsonResponse(payload)));

        var quote = await provider.GetLatestAsync(CancellationToken.None);

        Assert.Equal(858.01m, quote.Quote);
        Assert.Equal(new DateOnly(2026, 10, 26), quote.EffectiveDate);
        Assert.Equal("icbc", quote.Source);
    }

    [Fact]
    public async Task IcbcGoldQuote_WithEmptyRecordList_ThrowsUnavailable()
    {
        const string payload = """{"sysdate":"2026-10-26 10:26:51","TranErrorCode":"","rf":[],"TranErrorDisplayMsg":""}""";
        var provider = new IcbcGoldQuoteProvider(CreateClient(_ => JsonResponse(payload)));

        await Assert.ThrowsAsync<QuoteUnavailableException>(() => provider.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IcbcGoldQuote_WithTranError_ThrowsUnavailable()
    {
        const string payload = """{"sysdate":"2026-10-26 10:26:51","TranErrorCode":"ERR","rf":[],"TranErrorDisplayMsg":"系统繁忙"}""";
        var provider = new IcbcGoldQuoteProvider(CreateClient(_ => JsonResponse(payload)));

        var exception = await Assert.ThrowsAsync<QuoteUnavailableException>(() => provider.GetLatestAsync(CancellationToken.None));

        Assert.Equal("系统繁忙", exception.Message);
    }

    [Fact]
    public async Task EastmoneyStockSearch_IncludesExchangeTradedFundsAndTheirMarket()
    {
        const string payload = """
            {"QuotationCodeTable":{"Data":[
              {"Code":"159915","Name":"创业板ETF易方达","Classify":"Fund","SecurityTypeName":"基金","QuoteID":"0.159915"},
              {"Code":"510300","Name":"沪深300ETF华泰柏瑞","Classify":"Fund","SecurityTypeName":"基金","QuoteID":"1.510300"},
              {"Code":"601318","Name":"中国平安","Classify":"AStock","SecurityTypeName":"A股","QuoteID":"1.601318"},
              {"Code":"03147","Name":"X南方中创业","Classify":"HK","SecurityTypeName":"港股","QuoteID":"116.03147"},
              {"Code":"113050","Name":"南银转债","Classify":"Bond","SecurityTypeName":"可转债","QuoteID":"1.113050"},
              {"Code":"000001","Name":"上证指数","Classify":"Index","SecurityTypeName":"指数","QuoteID":"1.000001"},
              {"Code":"000001","Name":"华夏成长混合","Classify":"OTCFUND","SecurityTypeName":"基金","QuoteID":"150.000001"},
              {"Code":"900901","Name":"云赛B股","Classify":"BStock","SecurityTypeName":"沪B","QuoteID":"1.900901"}
            ],"Status":0,"Message":"成功"}}
            """;
        var provider = new EastmoneyStockQuoteProvider(
            CreateClient(_ => JsonResponse(payload)),
            Options.Create(new QuoteProviderOptions()));

        var results = await provider.SearchAsync("创业板", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(StockMarket.Shenzhen, results[0].Market);
        Assert.Equal("159915", results[0].Code);
        Assert.Equal(StockMarket.Shanghai, results[1].Market);
        Assert.Equal("510300", results[1].Code);
        Assert.Equal(StockMarket.Shanghai, results[2].Market);
        Assert.Equal("601318", results[2].Code);
    }

    [Fact]
    public async Task EastmoneyStockQuote_ParsesEtfPricePreviousCloseAndDate()
    {
        const string payload = """
            {"rc":0,"data":{"f43":2447,"f57":"159915","f58":"创业板ETF易方达","f59":3,"f60":2535,"f86":1793342907}}
            """;
        var provider = new EastmoneyStockQuoteProvider(
            CreateClient(_ => JsonResponse(payload)),
            Options.Create(new QuoteProviderOptions()));

        var quote = await provider.GetLatestAsync(StockMarket.Shenzhen, "159915", CancellationToken.None);

        Assert.Equal(2.447m, quote.Price);
        Assert.Equal(2.535m, quote.PreviousClose);
        Assert.Equal("创业板ETF易方达", quote.Name);
        Assert.Equal(new DateOnly(2026, 10, 30), quote.PriceDate);
        Assert.Equal("eastmoney", quote.Source);
    }

    [Fact]
    public async Task EastmoneyStockQuote_FallsBackToSecondaryHostWhenPrimaryFails()
    {
        const string payload = """
            {"rc":0,"data":{"f43":2447,"f57":"159915","f58":"创业板ETF易方达","f59":3,"f60":2535,"f86":1793342907}}
            """;
        var hosts = new List<string>();
        var client = new HttpClient(new StubHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);

            if (request.RequestUri.Host == "primary.test")
            {
                throw new HttpRequestException("An error occurred while sending the request.");
            }

            return JsonResponse(payload);
        }))
        {
            BaseAddress = new Uri("https://primary.test/")
        };
        var provider = new EastmoneyStockQuoteProvider(
            client,
            Options.Create(new QuoteProviderOptions { StockFallbackBaseUrl = "https://fallback.test/" }));

        var quote = await provider.GetLatestAsync(StockMarket.Shenzhen, "159915", CancellationToken.None);

        Assert.Equal(2.447m, quote.Price);
        Assert.Equal(["primary.test", "fallback.test"], hosts);
    }

    [Fact]
    public async Task EastmoneyStockQuote_WhenAllHostsFail_ThrowsFriendlyUnavailable()
    {
        var client = new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException("An error occurred while sending the request.")))
        {
            BaseAddress = new Uri("https://primary.test/")
        };
        var provider = new EastmoneyStockQuoteProvider(
            client,
            Options.Create(new QuoteProviderOptions { StockFallbackBaseUrl = "https://fallback.test/" }));

        var exception = await Assert.ThrowsAsync<QuoteUnavailableException>(
            () => provider.GetLatestAsync(StockMarket.Shenzhen, "159915", CancellationToken.None));

        Assert.Equal("股票行情服务暂时无法连接，请稍后重试。", exception.Message);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return new HttpClient(new StubHandler(responseFactory))
        {
            BaseAddress = new Uri("https://example.test/")
        };
    }

    private static HttpResponseMessage JsonResponse(string payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
