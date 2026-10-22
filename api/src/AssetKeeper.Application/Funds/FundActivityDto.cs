namespace AssetKeeper.Application.Funds;

public sealed record FundActivityDto(
    Guid Id,
    Guid PositionId,
    string PositionName,
    FundActivityType Type,
    decimal? Shares,
    decimal Amount,
    decimal CashEffect,
    decimal? NetValue,
    DateOnly BusinessDate,
    string? Note,
    DateTimeOffset CreatedAt,
    decimal Fee = 0m,
    Guid? LinkedTransactionId = null);
