namespace AssetKeeper.Domain.Ledgers;

public sealed class MutationReceipt
{
    private MutationReceipt()
    {
    }

    public Guid LedgerId { get; private set; }

    public Guid RequestId { get; private set; }

    public string Operation { get; private set; } = string.Empty;

    public string Fingerprint { get; private set; } = string.Empty;

    public string ResponseJson { get; private set; } = string.Empty;

    public string? BeforeJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MutationReceipt Create(Guid ledgerId, Guid requestId, string operation, string fingerprint, string responseJson, DateTimeOffset now, string? beforeJson)
    {
        return new MutationReceipt
        {
            LedgerId = ledgerId,
            RequestId = requestId,
            Operation = operation,
            Fingerprint = fingerprint,
            ResponseJson = responseJson,
            CreatedAt = now,
            BeforeJson = beforeJson
        };
    }
}
