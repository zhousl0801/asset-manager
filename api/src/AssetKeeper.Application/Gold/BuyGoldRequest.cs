namespace AssetKeeper.Application.Gold;

public sealed record BuyGoldRequest(
    decimal Grams,
    decimal Amount,
    decimal Quote,
    DateOnly QuoteDate,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null);
