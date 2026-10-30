using System.Globalization;
using System.Text.Json;
using AssetKeeper.Application.Quotes;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Infrastructure.Quotes;

public sealed class EastmoneyFundQuoteProvider : IFundQuoteProvider
{
    public const string SourceName = "eastmoney";

    private const string MobileHistoryPath = "FundMNewApi/FundMNHisNetList";

    private readonly HttpClient _httpClient;
    private readonly QuoteProviderOptions _options;

    public EastmoneyFundQuoteProvider(HttpClient httpClient, IOptions<QuoteProviderOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken)
    {
        return await GetQuoteAsync(code, null, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalFundSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx?m=1&key={Uri.EscapeDataString(query.Trim())}");
        request.Headers.Referrer = new Uri("https://fund.eastmoney.com/");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"基金搜索服务返回 {(int)response.StatusCode}。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("Datas", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<ExternalFundSearchResult>();

            foreach (var item in data.EnumerateArray())
            {
                var code = item.TryGetProperty("CODE", out var codeElement) ? codeElement.GetString()?.Trim() : null;
                var name = item.TryGetProperty("NAME", out var nameElement) ? nameElement.GetString()?.Trim() : null;

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var fundType = item.TryGetProperty("FundBaseInfo", out var baseInfo)
                    && baseInfo.ValueKind == JsonValueKind.Object
                    && baseInfo.TryGetProperty("FTYPE", out var typeElement)
                    ? typeElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
                var shareClass = FundProfileRules.DetectShareClass(name);
                var isQdii = FundProfileRules.IsQdii(name, fundType);
                results.Add(new ExternalFundSearchResult(
                    code,
                    name,
                    FundProfileRules.BaseName(name, shareClass),
                    shareClass,
                    fundType,
                    isQdii,
                    FundProfileRules.ConfirmationDelayDays(isQdii)));

                if (results.Count >= 20)
                {
                    break;
                }
            }

            return results;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new QuoteUnavailableException("基金搜索服务返回的数据无法解析。", exception);
        }
    }

    public async Task<ExternalFundQuote> GetForDateAsync(string code, DateOnly date, CancellationToken cancellationToken)
    {
        return await GetQuoteAsync(code, date, cancellationToken);
    }

    private async Task<ExternalFundQuote> GetQuoteAsync(string code, DateOnly? date, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new QuoteUnavailableException("基金代码为空，无法获取净值。");
        }

        var trimmedCode = code.Trim();
        var pageSize = date is null ? 1 : 100;
        Exception? lastError = null;

        // 一个站点不通就换下一个：主站与移动端接口在不同集群，字段一致。
        foreach (var endpoint in QuoteEndpoints(trimmedCode, pageSize))
        {
            try
            {
                return await RequestQuoteAsync(endpoint, trimmedCode, date, cancellationToken);
            }
            catch (Exception exception) when (exception is QuoteUnavailableException or HttpRequestException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                lastError = exception;
            }
        }

        if (lastError is QuoteUnavailableException unavailable)
        {
            throw new QuoteUnavailableException(unavailable.Message, unavailable);
        }

        throw new QuoteUnavailableException("基金净值服务暂时无法连接，请稍后重试。", lastError);
    }

    private async Task<ExternalFundQuote> RequestQuoteAsync(
        (FundQuoteFormat Format, string Url, Uri Referrer) endpoint,
        string code,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
        request.Headers.Referrer = endpoint.Referrer;

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"基金净值服务返回 {(int)response.StatusCode}。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!TryReadHistoryList(document.RootElement, endpoint.Format, out var list) || list.GetArrayLength() == 0)
            {
                throw new QuoteUnavailableException("基金净值服务未返回有效记录。");
            }

            var latest = date is null
                ? list[0]
                : list.EnumerateArray().FirstOrDefault(item =>
                    DateOnly.TryParseExact(item.GetProperty("FSRQ").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                    && parsedDate == date.Value);

            if (latest.ValueKind == JsonValueKind.Undefined)
            {
                throw new QuoteUnavailableException($"基金净值服务未返回 {date:yyyy-MM-dd} 的确认净值。");
            }

            var dateText = latest.GetProperty("FSRQ").GetString();
            var valueText = latest.GetProperty("DWJZ").GetString();

            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveDate)
                || !decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var netValue)
                || netValue <= 0)
            {
                throw new QuoteUnavailableException("基金净值服务返回的日期或净值无效。");
            }

            return new ExternalFundQuote(code, netValue, effectiveDate, SourceName, DateTimeOffset.UtcNow);
        }
        catch (QuoteUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new QuoteUnavailableException("基金净值服务返回的数据无法解析。", exception);
        }
    }

    private IEnumerable<(FundQuoteFormat Format, string Url, Uri Referrer)> QuoteEndpoints(string code, int pageSize)
    {
        var escaped = Uri.EscapeDataString(code);

        yield return (
            FundQuoteFormat.HistoryJson,
            $"{Normalize(_options.FundBaseUrl)}f10/lsjz?fundCode={escaped}&pageIndex=1&pageSize={pageSize}",
            new Uri("https://fundf10.eastmoney.com/"));

        yield return (
            FundQuoteFormat.MobileHistoryJson,
            $"{Normalize(_options.FundFallbackBaseUrl)}{MobileHistoryPath}?FCODE={escaped}&pageIndex=1&pageSize={pageSize}"
            + "&plat=Android&appType=ttjj&product=EFund&Version=1&deviceid=1",
            new Uri("https://fund.eastmoney.com/"));
    }

    private static string Normalize(string baseUrl)
    {
        return baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/";
    }

    private static bool TryReadHistoryList(JsonElement root, FundQuoteFormat format, out JsonElement list)
    {
        list = default;

        if (format == FundQuoteFormat.HistoryJson)
        {
            return root.TryGetProperty("Data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("LSJZList", out list)
                && list.ValueKind == JsonValueKind.Array;
        }

        return root.TryGetProperty("Datas", out list) && list.ValueKind == JsonValueKind.Array;
    }

    private enum FundQuoteFormat
    {
        HistoryJson,
        MobileHistoryJson
    }
}
