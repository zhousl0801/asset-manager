using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record BuyStockRequest(
    StockMarket Market,
    string Code,
    string? Name,
    decimal Shares,
    decimal Price,
    decimal Fee,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
