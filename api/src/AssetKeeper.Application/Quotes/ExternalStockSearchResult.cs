using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Quotes;

public sealed record ExternalStockSearchResult(
    StockMarket Market,
    string Code,
    string Name,
    string Currency = "CNY",
    decimal? Price = null,
    DateOnly? PriceDate = null);
