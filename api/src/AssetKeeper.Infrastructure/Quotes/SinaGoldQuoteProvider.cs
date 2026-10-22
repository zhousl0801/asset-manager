using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Infrastructure.Quotes;

// Reference quote only: SHFE gold continuous futures in CNY/g, not a platform accumulation-gold price.
public sealed partial class SinaGoldQuoteProvider : IGoldQuoteProvider
{
    public const string SourceName = "sina_shfe_au0";

    private readonly HttpClient _httpClient;

    public SinaGoldQuoteProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ExternalGoldQuote> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "list=nf_AU0");
        request.Headers.Referrer = new Uri("https://finance.sina.com.cn/");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"黄金参考行情服务返回 {(int)response.StatusCode}。");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var text = Encoding.Latin1.GetString(bytes);
        var match = QuotePattern().Match(text);

        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            throw new QuoteUnavailableException("黄金参考行情服务未返回有效报价。");
        }

        var fields = match.Groups[1].Value.Split(',');

        if (fields.Length < 18
            || !decimal.TryParse(fields[8], NumberStyles.Number, CultureInfo.InvariantCulture, out var quote)
            || quote <= 0)
        {
            throw new QuoteUnavailableException("黄金参考行情服务返回的报价无效。");
        }

        if (!TryParseDate(fields, out var effectiveDate))
        {
            throw new QuoteUnavailableException("黄金参考行情服务返回的报价日期无效。");
        }

        return new ExternalGoldQuote(quote, effectiveDate, SourceName, DateTimeOffset.UtcNow);
    }

    private static bool TryParseDate(string[] fields, out DateOnly effectiveDate)
    {
        foreach (var field in fields.Skip(15).Take(4))
        {
            if (DateOnly.TryParseExact(field, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out effectiveDate))
            {
                return true;
            }
        }

        effectiveDate = default;
        return false;
    }

    [GeneratedRegex("hq_str_nf_AU0=\"([^\"]*)\"")]
    private static partial Regex QuotePattern();
}
