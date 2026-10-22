namespace AssetKeeper.Application.Funds;

public sealed record FundQuoteRefreshDto(
    FundPositionDto Position,
    bool Updated,
    string Source,
    decimal NetValue,
    DateOnly EffectiveDate,
    DateTimeOffset ObtainedAt,
    DateTimeOffset LastAttemptAt);
