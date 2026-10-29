using System.Globalization;
using System.Text.Json;
using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Infrastructure.Quotes;

public sealed class EastmoneyFundQuoteProvider : IFundQuoteProvider
{
    public const string SourceName = "eastmoney";

    private readonly HttpClient _httpClient;

    public EastmoneyFundQuoteProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
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

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"f10/lsjz?fundCode={Uri.EscapeDataString(code.Trim())}&pageIndex=1&pageSize={(date is null ? 1 : 100)}");
        request.Headers.Referrer = new Uri("https://fundf10.eastmoney.com/");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"基金净值服务返回 {(int)response.StatusCode}。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("Data", out var data)
                || !data.TryGetProperty("LSJZList", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() == 0)
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

            return new ExternalFundQuote(code.Trim(), netValue, effectiveDate, SourceName, DateTimeOffset.UtcNow);
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
}
