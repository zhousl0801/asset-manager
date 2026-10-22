namespace AssetKeeper.Application.Funds;

public sealed record PendingFundArrivalDto(
    Guid TransactionId,
    Guid PositionId,
    string PositionName,
    decimal Shares,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note);
