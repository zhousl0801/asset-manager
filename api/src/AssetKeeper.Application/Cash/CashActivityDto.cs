using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Cash;

public sealed record CashActivityDto(
    Guid Id,
    CashActivityType Type,
    string Title,
    string? Note,
    decimal Amount,
    DateOnly BusinessDate,
    LedgerEntrySource? Source,
    AssetType? AssetType);
