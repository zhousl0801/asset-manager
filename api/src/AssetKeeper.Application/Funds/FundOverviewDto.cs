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
    bool HasEffectiveQuotes = true);
