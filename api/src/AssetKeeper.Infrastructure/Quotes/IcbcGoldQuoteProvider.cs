using System.Globalization;
using System.Text.Json;
using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Infrastructure.Quotes;

// ICBC accumulation-gold active price in CNY/g from the bank's public async endpoint.
// The response may be GBK-declared but the payload used here is plain ASCII JSON.
public sealed class IcbcGoldQuoteProvider : IGoldQuoteProvider
{
    public const string SourceName = "icbc";
    public const string ProductName = "积存金";

    private readonly HttpClient _httpClient;

    public IcbcGoldQuoteProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ExternalGoldQuote> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "servlet/AsynGetDataServlet");
        request.Headers.Referrer = new Uri("https://mybank.icbc.com.cn/");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tranCode"] = "A00622"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteUnavailableException($"工商银行积存金行情服务返回 {(int)response.StatusCode}。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var errorCode = ReadString(root, "TranErrorCode");

            if (!string.IsNullOrEmpty(errorCode))
            {
                var displayMessage = ReadString(root, "TranErrorDisplayMsg");
                throw new QuoteUnavailableException(string.IsNullOrWhiteSpace(displayMessage)
                    ? "工商银行积存金行情服务返回错误。"
                    : displayMessage);
            }

            if (!root.TryGetProperty("rf", out var records)
                || records.ValueKind != JsonValueKind.Array
                || records.GetArrayLength() == 0)
            {
                throw new QuoteUnavailableException("工商银行积存金行情服务未返回积存金价格。");
            }

            var record = PickGoldRecord(records);

            var priceText = ReadString(record, "ActivePrice");
            var dateText = ReadString(record, "ActiveDate");

            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var quote)
                || quote <= 0)
            {
                throw new QuoteUnavailableException("工商银行积存金行情服务返回的价格无效。");
            }

            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveDate))
            {
                throw new QuoteUnavailableException("工商银行积存金行情服务返回的价格日期无效。");
            }

            return new ExternalGoldQuote(quote, effectiveDate, SourceName, DateTimeOffset.UtcNow);
        }
        catch (QuoteUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new QuoteUnavailableException("工商银行积存金行情服务返回的数据无法解析。", exception);
        }
    }

    private static JsonElement PickGoldRecord(JsonElement records)
    {
        foreach (var item in records.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && string.Equals(ReadString(item, "ProductName"), ProductName, StringComparison.Ordinal))
            {
                return item;
            }
        }

        var first = records.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object);

        if (first.ValueKind == JsonValueKind.Undefined)
        {
            throw new QuoteUnavailableException("工商银行积存金行情服务未返回积存金价格。");
        }

        return first;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
