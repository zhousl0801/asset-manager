using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed record CreateLedgerEntryRequest(
    LedgerEntryKind Kind,
    string Category,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null);
