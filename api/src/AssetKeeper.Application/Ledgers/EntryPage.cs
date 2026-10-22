using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed record EntryPage(IReadOnlyList<LedgerEntry> Items, int TotalCount);
