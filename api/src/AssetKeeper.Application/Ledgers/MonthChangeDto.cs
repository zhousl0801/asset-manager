namespace AssetKeeper.Application.Ledgers;

public sealed record AssetChangeDto(string AssetType, decimal? Change, bool IsNew);

public sealed record MonthChangeDto(
    DateOnly From,
    DateOnly To,
    decimal? TotalChange,
    decimal LivingSurplus,
    decimal ProvidentDeposit,
    decimal? InvestmentProfit,
    decimal ReconciliationDifference,
    bool IsComplete,
    IReadOnlyList<string> Notes,
    IReadOnlyList<AssetChangeDto> AssetChanges,
    decimal? ComparableChange = null,
    decimal? ComparableBase = null,
    decimal? AddedRecords = null,
    decimal? MarketValueChange = null,
    decimal? NetDeposit = null,
    bool HasComparableData = false);
