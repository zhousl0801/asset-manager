namespace AssetKeeper.Application.Funds;

public sealed record PendingFundBuyDto(
    Guid TransactionId,
    Guid PositionId,
    string PositionName,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note);
