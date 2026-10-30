using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record StockQuoteRefreshDto(
    Guid PositionId,
    string Name,
    string Code,
    StockMarket Market,
    bool Updated,
    string Source,
    decimal Price,
    DateOnly PriceDate,
    decimal? PreviousClose,
    DateOnly? PreviousCloseDate,
    DateTimeOffset ObtainedAt,
    DateTimeOffset LastAttemptAt,
    decimal Value,
    decimal CashBalance);

public sealed record StockQuoteRefreshBatchDto(
    int Attempted,
    int Succeeded,
    int Failed,
    int Updated,
    IReadOnlyList<StockQuoteRefreshDto> Items,
    IReadOnlyList<string> Errors);
