namespace AssetKeeper.Application.Ledgers;

public sealed record AssetTrendPointDto(
    DateOnly Date,
    decimal? TotalAssets,
    decimal KnownTotalAssets,
    decimal? Cash,
    decimal? Provident,
    decimal? FundValue,
    decimal FundPending,
    decimal FundPendingArrival,
    decimal? GoldValue,
    decimal GoldPendingArrival,
    bool IsComplete,
    bool HasCash = false,
    bool HasProvident = false,
    bool HasFunds = false,
    bool HasGold = false,
    decimal? StockValue = null,
    bool HasStocks = false);

public sealed record AssetTrendDto(
    TrendRange Range,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AssetTrendPointDto> Points,
    IReadOnlyList<string> Notes);
