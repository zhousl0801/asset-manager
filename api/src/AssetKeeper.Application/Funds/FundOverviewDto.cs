namespace AssetKeeper.Application.Funds;

public sealed record FundOverviewDto(
    decimal TotalValue,
    decimal TotalCost,
    decimal HoldingProfit,
    decimal TotalRealizedProfit,
    decimal CashBalance,
    IReadOnlyList<FundPositionDto> Positions,
    IReadOnlyList<FundActivityDto> Activities,
    bool HistoryConfirmed = true,
    bool HasEffectiveQuotes = true,
    decimal PendingAmount = 0m,
    IReadOnlyList<PendingFundBuyDto>? PendingBuys = null,
    decimal PendingArrivalAmount = 0m,
    IReadOnlyList<PendingFundSellDto>? SellApplications = null,
    IReadOnlyList<PendingFundArrivalDto>? PendingArrivals = null);
