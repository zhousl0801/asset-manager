namespace AssetKeeper.Application.Ledgers;

public sealed record EntryStatsDto(
    EntryPeriod Period,
    DateOnly Anchor,
    DateOnly From,
    DateOnly To,
    int CoveredDays,
    decimal Income,
    decimal Expense,
    decimal IncomeDailyAverage,
    decimal ExpenseDailyAverage,
    IReadOnlyList<EntryStatsPointDto> Points,
    IReadOnlyList<EntryStatsCategoryDto> IncomeCategories,
    IReadOnlyList<EntryStatsCategoryDto> ExpenseCategories,
    string? Note);

public sealed record EntryStatsPointDto(
    DateOnly Date,
    string Label,
    decimal Income,
    decimal Expense);

public sealed record EntryStatsCategoryDto(
    string Category,
    decimal Amount,
    int Count,
    decimal Share);
