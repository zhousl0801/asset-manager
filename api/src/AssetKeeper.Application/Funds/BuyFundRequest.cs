namespace AssetKeeper.Application.Funds;

public sealed record BuyFundRequest(
    Guid PositionId,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    decimal Fee = 0m);
