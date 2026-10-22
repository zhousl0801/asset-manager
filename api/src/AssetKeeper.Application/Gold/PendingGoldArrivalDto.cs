namespace AssetKeeper.Application.Gold;

public sealed record PendingGoldArrivalDto(
    Guid TransactionId,
    decimal Grams,
    decimal Amount,
    DateOnly BusinessDate,
    string? Note);
