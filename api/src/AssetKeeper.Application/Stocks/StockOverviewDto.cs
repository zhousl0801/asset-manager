using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed record StockOverviewDto(
    bool IsConfigured,
    int PositionCount,
    decimal TotalCost,
    decimal TotalValue,
    decimal? TodayProfit,
    bool TodayProfitComplete,
    DateOnly? TodayProfitDate,
    decimal HoldingProfit,
    decimal RealizedProfit,
    decimal CashBalance,
    IReadOnlyList<StockPositionDto> Positions,
    IReadOnlyList<StockActivityDto> Activities,
    bool HistoryConfirmed = true,
    bool HasEffectiveQuotes = true);
