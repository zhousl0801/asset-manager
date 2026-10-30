using System.Globalization;
using System.Text.Json;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Infrastructure.Quotes;

// Eastmoney public quote endpoints. A-share quotes are delayed/real-time depending on the
// endpoint policy; the app only stores the latest observation and never intraday history.
public sealed class EastmoneyStockQuoteProvider : IStockQuoteProvider
{
    public const string SourceName = "eastmoney";

    private readonly HttpClient _httpClient;
    private readonly QuoteProviderOptions _options;

    public EastmoneyStockQuoteProvider(HttpClient httpClient, IOptions<QuoteProviderOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ExternalStockQuote> GetLatestAsync(
        StockMarket market,
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new QuoteUnavailableException("股票代码为空，无法获取行情。");
        }

        var normalizedCode = code.Trim().ToUpperInvariant();
        using var response = await SendQuoteRequestAsync(
            $"api/qt/stock/get?secid={SecId(market, normalizedCode)}&fields=f43,f57,f58,f59,f60,f86",
            cancellationToken);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new QuoteUnavailableException("股票行情服务未返回有效记录。");
            }

            var precision = ReadInt(data, "f59") is int scale && scale is >= 0 and <= 6 ? scale : 2;
            var price = ReadScaledDecimal(data, "f43", precision);
            var previousClose = ReadScaledDecimal(data, "f60", precision);
            var name = ReadString(data, "f58") ?? normalizedCode;
            var quoteCode = ReadString(data, "f57") ?? normalizedCode;
            var timestamp = ReadLong(data, "f86");
            var priceDate = timestamp is > 0
                ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).ToOffset(TimeSpan.FromHours(8)).Date)
                : DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

            if (price is null || price <= 0m)
            {
                throw new QuoteUnavailableException("股票行情服务返回的最新价无效。");
            }

            if (previousClose is <= 0m)
            {
                previousClose = null;
            }

            return new ExternalStockQuote(
                market,
                quoteCode,
                name,
                price.Value,
                priceDate,
                previousClose,
                null,
                SourceName,
                DateTimeOffset.UtcNow);
        }
        catch (QuoteUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new QuoteUnavailableException("股票行情服务返回的数据无法解析。", exception);
        }
    }

    public async Task<IReadOnlyList<ExternalStockSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://searchapi.eastmoney.com/api/suggest/get?input={Uri.EscapeDataString(query.Trim())}&type=14&count=20");
        request.Headers.Referrer = new Uri("https://quote.eastmoney.com/");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"股票搜索服务返回 {(int)response.StatusCode}。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("QuotationCodeTable", out var table)
                || !table.TryGetProperty("Data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<ExternalStockSearchResult>();

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var code = ReadString(item, "Code");
                var name = ReadString(item, "Name");
                var quoteId = ReadString(item, "QuoteID") ?? ReadString(item, "ID");
                var securityType = ReadString(item, "SecurityTypeName") ?? string.Empty;
                var classify = ReadString(item, "Classify") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var normalizedCode = code.Trim().ToUpperInvariant();

                if (!IsExchangeTradedSecurity(normalizedCode, securityType, classify))
                {
                    continue;
                }

                var market = ParseMarket(normalizedCode, quoteId);

                results.Add(new ExternalStockSearchResult(market, normalizedCode, name.Trim()));

                if (results.Count >= 20)
                {
                    break;
                }
            }

            return results;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new QuoteUnavailableException("股票搜索服务返回的数据无法解析。", exception);
        }
    }

    private static string SecId(StockMarket market, string code)
    {
        return market == StockMarket.Shanghai ? $"1.{code}" : $"0.{code}";
    }

    // 主站不通时依次降级到备用站点：连接失败、超时和非 2xx 都换下一个站点重试。
    private async Task<HttpResponseMessage> SendQuoteRequestAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (var baseUrl in QuoteBaseUrls())
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{relativeUrl}");
            request.Headers.Referrer = new Uri("https://quote.eastmoney.com/");

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                lastError = new QuoteUnavailableException($"股票行情服务返回 {(int)response.StatusCode}。");
                response.Dispose();
            }
            catch (HttpRequestException exception)
            {
                lastError = exception;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
            }
        }

        throw new QuoteUnavailableException("股票行情服务暂时无法连接，请稍后重试。", lastError);
    }

    private IEnumerable<string> QuoteBaseUrls()
    {
        var primary = _httpClient.BaseAddress?.ToString() ?? _options.StockBaseUrl;
        var candidates = new[] { primary, _options.StockFallbackBaseUrl };

        return candidates
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.EndsWith('/') ? url : $"{url}/")
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // 支持范围：沪深北 A 股，以及在沪深两市上市、按股票方式交易的场内基金（ETF、LOF、封闭式基金）。
    // 指数、B 股、场外基金、港股、美股、债券、可转债和回购都不进入股票模块。
    private static bool IsExchangeTradedSecurity(string code, string securityType, string classify)
    {
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            return false;
        }

        if (IsAShareLabel(securityType, classify))
        {
            return true;
        }

        if (IsFundLabel(securityType, classify))
        {
            return IsExchangeTradedFundCode(code);
        }

        // 接口给了类型但既不是 A 股也不是基金：指数、B 股、债券、可转债、港股、美股、场外基金全部排除。
        if (!string.IsNullOrWhiteSpace(securityType) || !string.IsNullOrWhiteSpace(classify))
        {
            return false;
        }

        // 类型字段缺失时才按代码段兜底。
        return IsAShareCode(code) || IsExchangeTradedFundCode(code);
    }

    private static bool IsAShareLabel(string securityType, string classify)
    {
        return securityType.Contains("股票", StringComparison.OrdinalIgnoreCase)
            || securityType.Contains("A股", StringComparison.OrdinalIgnoreCase)
            || securityType is "深A" or "沪A" or "京A"
            || classify.Contains("AStock", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFundLabel(string securityType, string classify)
    {
        // 场外基金也有“基金”类型名，靠 OTCFUND 区分。
        if (classify.Contains("OTCFUND", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return securityType.Contains("基金", StringComparison.OrdinalIgnoreCase)
            || classify.Contains("Fund", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAShareCode(string code)
    {
        return code.StartsWith('6') || code.StartsWith('0') || code.StartsWith('3')
            || code.StartsWith('4') || code.StartsWith('8') || code.StartsWith("92");
    }

    private static bool IsExchangeTradedFundCode(string code)
    {
        // 沪市 5xxxxx（ETF 51/56/58、LOF 502、REITs 508）；深市 15xxxx（ETF）、16xxxx（LOF）、18xxxx（封闭式基金）。
        return code.StartsWith('5')
            || code.StartsWith("15") || code.StartsWith("16") || code.StartsWith("18");
    }

    private static StockMarket ParseMarket(string code, string? quoteId)
    {
        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            if (quoteId.StartsWith("1.", StringComparison.Ordinal))
            {
                return StockMarket.Shanghai;
            }

            if (quoteId.StartsWith("0.", StringComparison.Ordinal))
            {
                return code.StartsWith('4') || code.StartsWith('8') || code.StartsWith("92")
                    ? StockMarket.Beijing
                    : StockMarket.Shenzhen;
            }
        }

        if (code.StartsWith('4') || code.StartsWith('8') || code.StartsWith("92"))
        {
            return StockMarket.Beijing;
        }

        if (code.StartsWith('6') || code.StartsWith('9') || code.StartsWith('5'))
        {
            return StockMarket.Shanghai;
        }

        return StockMarket.Shenzhen;
    }

    private static decimal? ReadScaledDecimal(JsonElement element, string propertyName, int precision)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number / (decimal)Math.Pow(10, precision);
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number / (decimal)Math.Pow(10, precision);
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var result) ? result : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }
}
