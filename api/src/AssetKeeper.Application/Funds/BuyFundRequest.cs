namespace AssetKeeper.Application.Funds;

public sealed record BuyFundRequest(
    Guid PositionId,
    decimal Shares,
    decimal Amount,
    decimal NetValue,
    DateOnly NetValueDate,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null);
