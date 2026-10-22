namespace AssetKeeper.Application.Ledgers;

public sealed record EntryMutationResultDto(LedgerEntryDto Entry, decimal CashBalance);
