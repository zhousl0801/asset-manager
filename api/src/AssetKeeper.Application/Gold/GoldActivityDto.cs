namespace AssetKeeper.Application.Gold;

public sealed record GoldActivityDto(
    Guid Id,
    GoldActivityType Type,
    decimal? Grams,
    decimal Amount,
    decimal CashEffect,
    decimal? Quote,
    DateOnly BusinessDate,
    string? Note,
    DateTimeOffset CreatedAt);
