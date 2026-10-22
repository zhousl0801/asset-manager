namespace AssetKeeper.Application.Ledgers;

public sealed record MonthChangeDto(
    DateOnly From,
    DateOnly To,
    decimal? TotalChange,
    decimal LivingSurplus,
    decimal ProvidentDeposit,
    decimal? InvestmentProfit,
    decimal ReconciliationDifference,
    bool IsComplete,
    IReadOnlyList<string> Notes);
