namespace AssetKeeper.Application.Funds;

public sealed record FundPositionDto(
    Guid Id,
    string Name,
    string? Code,
    string? ShareClass,
    string? Platform,
    decimal Shares,
    decimal Cost,
    decimal NetValue,
    DateOnly NetValueDate,
    decimal Value,
    decimal HoldingProfit,
    decimal RealizedProfit,
    bool HistoryConfirmed = true,
    bool HasEffectiveQuote = true,
    DateTimeOffset? LastQuoteAttemptAt = null,
    string? LastQuoteSource = null,
    string? LastQuoteError = null);
