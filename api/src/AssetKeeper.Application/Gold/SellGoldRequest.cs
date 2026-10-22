namespace AssetKeeper.Application.Gold;

public sealed record SellGoldRequest(
    decimal Grams,
    decimal NetAmount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null,
    GoldSellSettlement Settlement = GoldSellSettlement.Received);
