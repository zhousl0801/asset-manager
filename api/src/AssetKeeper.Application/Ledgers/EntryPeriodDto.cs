namespace AssetKeeper.Application.Ledgers;

public sealed record EntryPeriodDto(
    EntryPeriod Period,
    DateOnly From,
    DateOnly To,
    decimal Income,
    decimal Expense,
    decimal Surplus,
    IReadOnlyList<LedgerEntryDto> Entries,
    int Page,
    int PageSize,
    int TotalCount);
