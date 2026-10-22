namespace AssetKeeper.Application.Funds;

public sealed record SellFundRequest(
    Guid PositionId,
    decimal Shares,
    decimal NetAmount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null);
