namespace AssetKeeper.Application.Gold;

public sealed record SaveGoldHoldingRequest(
    decimal Grams,
    decimal Cost,
    decimal Quote,
    DateOnly QuoteDate,
    string? Note,
    Guid RequestId,
    int ExpectedVersion = 0,
    DateOnly? HoldingDate = null);
