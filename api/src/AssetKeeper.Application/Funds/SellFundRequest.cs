namespace AssetKeeper.Application.Funds;

public sealed record SellFundRequest(
    Guid PositionId,
    decimal Shares,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
