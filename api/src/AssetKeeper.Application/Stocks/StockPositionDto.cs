using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record StockPositionDto(
    Guid Id,
    StockMarket Market,
    string Code,
    string Name,
    string Currency,
    decimal Shares,
    decimal Cost,
    decimal? AverageCost,
    decimal Price,
    DateOnly PriceDate,
    decimal? PreviousClose,
    DateOnly? PreviousCloseDate,
    decimal Value,
    decimal HoldingProfit,
    decimal RealizedProfit,
    decimal? TodayProfit,
    bool TodayProfitComplete,
    bool HistoryConfirmed = true,
    bool HasEffectiveQuote = true,
    int Version = 0,
    DateTimeOffset? LastQuoteAttemptAt = null,
    string? LastQuoteSource = null,
    string? LastQuoteError = null);
