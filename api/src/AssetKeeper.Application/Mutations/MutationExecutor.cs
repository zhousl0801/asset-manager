using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.History;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Mutations;

// The receipt and every related balance/holding change share one SaveChanges transaction.
public sealed class MutationExecutor
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public MutationExecutor(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        string operation,
        Guid requestId,
        object request,
        Func<Task<TResponse>> execute,
        Func<TResponse, bool> succeeded,
        Func<TResponse, TResponse> replay,
        Func<string, TResponse> conflict,
        Func<string, TResponse> invalid,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return invalid("请求标识无效。");
        }

        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return await execute();
        }

        var fingerprint = Fingerprint(request);
        var receipt = await _ledgerRepository.FindMutationReceiptAsync(ledger.Id, requestId, cancellationToken);

        if (receipt is not null)
        {
            if (receipt.Operation != operation || receipt.Fingerprint != fingerprint)
            {
                return conflict("该请求标识已用于其他内容，请核对原记录后重新操作。");
            }

            return replay(JsonSerializer.Deserialize<TResponse>(receipt.ResponseJson, JsonOptions)!);
        }

        // Pre-G0 records have no complete request snapshot. Never infer a match or apply twice.
        if (await _ledgerRepository.FindEntryByRequestIdAsync(ledger.Id, requestId, cancellationToken) is not null
            || await _ledgerRepository.FindAssetTransactionByRequestIdAsync(ledger.Id, requestId, cancellationToken) is not null)
        {
            return conflict("该历史请求已入账，缺少完整请求快照，请查看原记录，勿重复保存。");
        }

        var beforeJson = await CaptureBeforeAsync(operation, ledger.Id, request, cancellationToken);
        TResponse result;

        try
        {
            result = await execute();
        }
        catch (HistoryValidationException exception)
        {
            _ledgerRepository.DiscardChanges();
            return invalid(exception.Message);
        }

        if (!succeeded(result))
        {
            _ledgerRepository.DiscardChanges();
            return result;
        }

        ledger.Touch(_dates.UtcNow);
        await _ledgerRepository.AddMutationReceiptAsync(MutationReceipt.Create(
            ledger.Id,
            requestId,
            operation,
            fingerprint,
            JsonSerializer.Serialize(result, JsonOptions),
            _dates.UtcNow, beforeJson), cancellationToken);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (ConcurrencyConflictException)
        {
            _ledgerRepository.DiscardChanges();
            return conflict("数据已被其他操作修改，请核实保存结果或刷新后重试。");
        }
    }

    private async Task<string?> CaptureBeforeAsync(string operation, Guid ledgerId, object request, CancellationToken cancellationToken)
    {
        if (operation is "entry.update" or "entry.reverse")
        {
            var id = JsonSerializer.SerializeToElement(request, JsonOptions).GetProperty("entryId").GetGuid();
            var entry = await _ledgerRepository.GetEntryAsync(id, cancellationToken);

            if (entry is not null && entry.LedgerId == ledgerId)
            {
                return JsonSerializer.Serialize(new { entry.Id, entry.Kind, entry.Category, entry.Amount, entry.BusinessDate, entry.Note, entry.Status, entry.UpdatedAt, entry.ReversedAt, entry.ReversalReason }, JsonOptions);
            }
        }

        if (operation == "history.change")
        {
            var data = await _ledgerRepository.GetReplayDataAsync(ledgerId, cancellationToken);
            return JsonSerializer.Serialize(new { Request = request, Facts = BusinessFact.From(data), data.Ledger.CashAccount.OpeningBalance, data.Ledger.CashAccount.OpeningDate, data.Prices }, JsonOptions);
        }

        if (operation == "gold.holding")
        {
            var holding = await _ledgerRepository.GetGoldHoldingAsync(ledgerId, cancellationToken);

            if (holding is not null)
            {
                return JsonSerializer.Serialize(new { holding.Grams, holding.Cost, holding.Quote, holding.QuoteDate, holding.RealizedProfit, holding.Version }, JsonOptions);
            }
        }

        return null;
    }

    private static string Fingerprint(object request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, JsonSerializer.SerializeToElement(request, JsonOptions));
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString()?.Trim());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
