using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed record UpdateLedgerEntryRequest(
    LedgerEntryKind Kind,
    string Category,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId = default,
    Guid? RelatedCashAdjustmentId = null);
