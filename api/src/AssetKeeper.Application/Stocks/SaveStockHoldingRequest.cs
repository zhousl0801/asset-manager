using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record SaveStockHoldingRequest(
    StockMarket Market,
    string Code,
    string Name,
    decimal Shares,
    decimal Cost,
    decimal Price,
    DateOnly PriceDate,
    DateOnly HoldingDate,
    string? Note,
    Guid RequestId,
    int ExpectedVersion = 0,
    Guid? PositionId = null);
