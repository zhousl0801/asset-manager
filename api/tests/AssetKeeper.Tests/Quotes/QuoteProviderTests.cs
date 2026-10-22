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
    public async Task SinaGoldQuote_ParsesCnyPerGramReference()
    {
        const string payload = "var hq_str_nf_AU0=\"gold,141923,890.180,893.080,875.840,0.000,883.220,883.260,883.280,0.000,890.060,1,1,209927.000,231146,SH,AU,2026-10-22,1\";";
        var provider = new SinaGoldQuoteProvider(CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes(payload))
        }));

        var quote = await provider.GetLatestAsync(CancellationToken.None);

        Assert.Equal(883.280m, quote.Quote);
        Assert.Equal(new DateOnly(2026, 10, 22), quote.EffectiveDate);
        Assert.Equal("sina_shfe_au0", quote.Source);
    }

    [Fact]
    public async Task SinaGoldQuote_WithEmptyPayload_ThrowsUnavailable()
    {
        const string payload = "var hq_str_nf_AU0=\"\";";
        var provider = new SinaGoldQuoteProvider(CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes(payload))
        }));

        await Assert.ThrowsAsync<QuoteUnavailableException>(() => provider.GetLatestAsync(CancellationToken.None));
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
