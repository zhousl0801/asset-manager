namespace AssetKeeper.Application.Ledgers;

public sealed record MonthSummaryDto(decimal Income, decimal Expense, decimal Surplus, bool HasRecords);
