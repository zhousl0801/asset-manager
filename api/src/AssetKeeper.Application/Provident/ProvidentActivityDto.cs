namespace AssetKeeper.Application.Provident;

public sealed record ProvidentActivityDto(
    Guid Id,
    ProvidentActivityType Type,
    decimal Amount,
    decimal BalanceAfter,
    DateOnly BusinessDate,
    string? Note,
    DateTimeOffset CreatedAt);
