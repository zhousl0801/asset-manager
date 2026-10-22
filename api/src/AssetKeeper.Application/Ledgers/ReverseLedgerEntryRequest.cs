namespace AssetKeeper.Application.Ledgers;

public sealed record ReverseLedgerEntryRequest(string? Reason, Guid RequestId = default);
