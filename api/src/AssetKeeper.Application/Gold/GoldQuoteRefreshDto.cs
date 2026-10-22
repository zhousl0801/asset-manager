namespace AssetKeeper.Application.Gold;

public sealed record GoldQuoteRefreshDto(
    bool Updated,
    string Source,
    decimal Quote,
    DateOnly QuoteDate,
    DateTimeOffset ObtainedAt,
    DateTimeOffset LastAttemptAt,
    decimal Value,
    decimal CashBalance);
