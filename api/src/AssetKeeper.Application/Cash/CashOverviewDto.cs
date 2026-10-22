namespace AssetKeeper.Application.Cash;

public sealed record CashOverviewDto(
    decimal Balance,
    DateOnly AsOfDate,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal Inflow,
    decimal Outflow,
    decimal AdjustmentTotal,
    decimal NetChange,
    IReadOnlyList<CashActivityDto> Activities,
    int Version);
