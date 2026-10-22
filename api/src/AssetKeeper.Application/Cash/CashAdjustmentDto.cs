namespace AssetKeeper.Application.Cash;

public sealed record CashAdjustmentDto(
    Guid Id,
    DateOnly BusinessDate,
    decimal PreviousBalance,
    decimal NewBalance,
    decimal Difference,
    string Reason,
    DateTimeOffset CreatedAt);
