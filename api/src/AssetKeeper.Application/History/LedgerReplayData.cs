using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.History;

public sealed record LedgerReplayData(
    Ledger Ledger,
    IReadOnlyList<LedgerEntry> Entries,
    IReadOnlyList<CashAdjustment> Adjustments,
    IReadOnlyList<AssetTransaction> Transactions,
    IReadOnlyList<PriceObservation> Prices,
    IReadOnlyList<FundPosition> Positions,
    IReadOnlyList<StockPosition> Stocks,
    ProvidentFundAccount? Provident,
    GoldHolding? Gold);
