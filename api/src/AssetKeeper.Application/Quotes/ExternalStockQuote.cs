using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Quotes;

public sealed record ExternalStockQuote(
    StockMarket Market,
    string Code,
    string Name,
    decimal Price,
    DateOnly PriceDate,
    decimal? PreviousClose,
    DateOnly? PreviousCloseDate,
    string Source,
    DateTimeOffset ObtainedAt);
