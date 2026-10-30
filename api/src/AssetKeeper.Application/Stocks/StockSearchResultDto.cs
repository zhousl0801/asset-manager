using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record StockSearchResultDto(
    StockMarket Market,
    string Code,
    string Name,
    string Currency = "CNY",
    decimal? Price = null,
    DateOnly? PriceDate = null);
