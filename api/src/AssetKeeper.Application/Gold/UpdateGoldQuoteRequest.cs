namespace AssetKeeper.Application.Gold;

public sealed record UpdateGoldQuoteRequest(
    decimal Quote,
    DateOnly QuoteDate,
    Guid RequestId);
