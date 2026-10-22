namespace AssetKeeper.Application.Gold;

public sealed record GoldOverviewDto(
    bool IsConfigured,
    decimal Grams,
    decimal Cost,
    decimal Quote,
    DateOnly? QuoteDate,
    decimal Value,
    decimal? AverageCost,
    decimal HoldingProfit,
    decimal RealizedProfit,
    decimal CashBalance,
    IReadOnlyList<GoldActivityDto> Activities,
    int Version,
    bool HistoryConfirmed = true,
    bool HasEffectiveQuote = true,
    decimal PendingArrivalAmount = 0m,
    IReadOnlyList<PendingGoldArrivalDto>? PendingArrivals = null);
