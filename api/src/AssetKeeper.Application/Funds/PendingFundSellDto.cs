namespace AssetKeeper.Application.Funds;

public sealed record PendingFundSellDto(
    Guid TransactionId,
    Guid PositionId,
    string PositionName,
    decimal Shares,
    DateOnly BusinessDate,
    string? Note,
    bool CanCancel);
