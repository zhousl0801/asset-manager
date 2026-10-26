using System.Net;
using System.Text;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Infrastructure.Quotes;

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
