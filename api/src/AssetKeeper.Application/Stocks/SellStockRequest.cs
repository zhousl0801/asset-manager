namespace AssetKeeper.Application.Stocks;

public sealed record SellStockRequest(
    Guid PositionId,
    decimal Shares,
    decimal Price,
    decimal Fee,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
