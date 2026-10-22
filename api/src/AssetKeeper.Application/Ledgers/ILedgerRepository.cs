using AssetKeeper.Application.History;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public interface ILedgerRepository
{
    Task<IReadOnlyList<HistoryAuditDto>> GetHistoryAuditAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<LedgerReplayData> GetReplayDataAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task AddPriceObservationAsync(PriceObservation price, CancellationToken cancellationToken);

    Task<PriceObservation?> GetLatestPriceObservationAsync(
        Guid ledgerId,
        AssetType assetType,
        Guid? positionId,
        CancellationToken cancellationToken);

    void DiscardChanges();

    Task<MutationReceipt?> FindMutationReceiptAsync(Guid ledgerId, Guid requestId, CancellationToken cancellationToken);

    Task AddMutationReceiptAsync(MutationReceipt receipt, CancellationToken cancellationToken);

    Task<EntryTotals> GetEntryTotalsAsync(Guid ledgerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<EntryPage> GetEntryPageAsync(Guid ledgerId, DateOnly from, DateOnly to, LedgerEntryKind? kind, string? category, string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetTransaction>> GetAllAssetTransactionsAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(CancellationToken cancellationToken);

    Task<Ledger?> GetAsync(CancellationToken cancellationToken);

    Task<Ledger?> GetForUpdateAsync(CancellationToken cancellationToken);

    Task AddAsync(Ledger ledger, CashAccount cashAccount, CancellationToken cancellationToken);

    Task<LedgerEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken);

    Task<LedgerEntry?> FindEntryByRequestIdAsync(Guid ledgerId, Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task AddEntryAsync(LedgerEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntryCategory>> GetCategoriesAsync(
        Guid ledgerId,
        CancellationToken cancellationToken);

    Task<EntryCategory?> GetCategoryAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string name,
        CancellationToken cancellationToken);

    Task<EntryCategory?> GetCategoryForUpdateAsync(
        Guid ledgerId,
        Guid categoryId,
        CancellationToken cancellationToken);

    Task<EntryCategory?> GetCategoryBySystemKeyAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string systemKey,
        CancellationToken cancellationToken);

    Task AddCategoryAsync(EntryCategory category, CancellationToken cancellationToken);

    Task AddCategoriesAsync(IReadOnlyList<EntryCategory> categories, CancellationToken cancellationToken);

    Task RenameEntryCategoryAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string oldName,
        string newName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CashAdjustment>> GetCashAdjustmentsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task AddCashAdjustmentAsync(CashAdjustment adjustment, CancellationToken cancellationToken);

    Task<ProvidentFundAccount?> GetProvidentFundAccountAsync(
        Guid ledgerId,
        CancellationToken cancellationToken);

    Task<ProvidentFundAccount?> GetProvidentFundAccountForUpdateAsync(
        Guid ledgerId,
        CancellationToken cancellationToken);

    Task AddProvidentFundAccountAsync(
        ProvidentFundAccount account,
        CancellationToken cancellationToken);

    Task<GoldHolding?> GetGoldHoldingAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<GoldHolding?> GetGoldHoldingForUpdateAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task AddGoldHoldingAsync(GoldHolding holding, CancellationToken cancellationToken);

    Task<IReadOnlyList<FundPosition>> GetFundPositionsAsync(
        Guid ledgerId,
        CancellationToken cancellationToken);

    Task<FundPosition?> GetFundPositionForUpdateAsync(
        Guid ledgerId,
        Guid positionId,
        CancellationToken cancellationToken);

    Task AddFundPositionAsync(FundPosition position, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetTransaction>> GetAssetTransactionsAsync(
        Guid ledgerId,
        AssetType assetType,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetTransaction>> GetAssetCashTransactionsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task AddAssetTransactionAsync(AssetTransaction transaction, CancellationToken cancellationToken);

    Task<AssetTransaction?> GetAssetTransactionForUpdateAsync(
        Guid ledgerId,
        Guid transactionId,
        CancellationToken cancellationToken);

    Task<AssetTransaction?> FindAssetTransactionByRequestIdAsync(
        Guid ledgerId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
