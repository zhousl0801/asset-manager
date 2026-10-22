namespace AssetKeeper.Application.Quotes;

public sealed record ExternalFundQuote(
    string Code,
    decimal NetValue,
    DateOnly EffectiveDate,
    string Source,
    DateTimeOffset ObtainedAt);
