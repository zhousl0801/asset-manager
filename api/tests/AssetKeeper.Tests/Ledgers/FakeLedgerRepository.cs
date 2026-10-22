using System.Reflection;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Tests.Ledgers;

internal sealed class FakeLedgerRepository : ILedgerRepository
{
    private readonly List<PriceObservation> _prices = new();
    private Dictionary<object, Dictionary<PropertyInfo, object?>> _saved = new();
    private readonly List<object> _savedObjects = new();
    private ProvidentFundAccount? _savedProvident;
    private GoldHolding? _savedGold;

    private readonly List<MutationReceipt> _receipts = new();

    private readonly List<LedgerEntry> _entries = new();
    private readonly List<CashAdjustment> _adjustments = new();
    private readonly List<FundPosition> _fundPositions = new();
    private readonly List<AssetTransaction> _assetTransactions = new();

    public Ledger? Ledger { get; private set; }

    public CashAccount? CashAccount { get; private set; }

    public ProvidentFundAccount? ProvidentAccount { get; private set; }

    public GoldHolding? GoldHolding { get; private set; }

    public IReadOnlyList<MutationReceipt> MutationReceipts => _receipts;

    public int SaveCount { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public IReadOnlyList<CashAdjustment> Adjustments => _adjustments;

    public IReadOnlyList<FundPosition> FundPositions => _fundPositions;

    public IReadOnlyList<AssetTransaction> AssetTransactions => _assetTransactions;

    public Task<IReadOnlyList<HistoryAuditDto>> GetHistoryAuditAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<HistoryAuditDto>>(_receipts.Where(receipt => receipt.LedgerId == ledgerId && receipt.Operation == "history.change")
            .OrderByDescending(receipt => receipt.CreatedAt).Take(100).Select(receipt => new HistoryAuditDto(receipt.RequestId, receipt.CreatedAt, receipt.BeforeJson)).ToList());
    }

    public Task<LedgerReplayData> GetReplayDataAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new LedgerReplayData(Ledger!, _entries, _adjustments, _assetTransactions, _prices, _fundPositions, ProvidentAccount, GoldHolding));
    }

    public Task AddPriceObservationAsync(PriceObservation price, CancellationToken cancellationToken)
    {
        _prices.Add(price);
        return Task.CompletedTask;
    }

    public void DiscardChanges()
    {
        foreach (var item in _saved)
        {
            foreach (var property in item.Value)
            {
                property.Key.SetValue(item.Key, property.Value);
            }
        }

        _entries.RemoveAll(item => !_savedObjects.Contains(item));
        _adjustments.RemoveAll(item => !_savedObjects.Contains(item));
        _assetTransactions.RemoveAll(item => !_savedObjects.Contains(item));
        _fundPositions.RemoveAll(item => !_savedObjects.Contains(item));
        _receipts.RemoveAll(item => !_savedObjects.Contains(item));
        _prices.RemoveAll(item => !_savedObjects.Contains(item));
        ProvidentAccount = _savedProvident;
        GoldHolding = _savedGold;
    }

    public Task<MutationReceipt?> FindMutationReceiptAsync(Guid ledgerId, Guid requestId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_receipts.SingleOrDefault(receipt => receipt.LedgerId == ledgerId && receipt.RequestId == requestId));
    }

    public Task AddMutationReceiptAsync(MutationReceipt receipt, CancellationToken cancellationToken)
    {
        _receipts.Add(receipt);
        return Task.CompletedTask;
    }

    public async Task<EntryTotals> GetEntryTotalsAsync(Guid ledgerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var entries = await GetEntriesAsync(ledgerId, from, to, cancellationToken);
        return new EntryTotals(entries.Where(entry => entry.Kind == LedgerEntryKind.Income).Sum(entry => entry.Amount), entries.Where(entry => entry.Kind == LedgerEntryKind.Expense).Sum(entry => entry.Amount), entries.Count);
    }

    public async Task<EntryPage> GetEntryPageAsync(Guid ledgerId, DateOnly from, DateOnly to, LedgerEntryKind? kind, string? category, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var entries = await GetEntriesAsync(ledgerId, from, to, cancellationToken);
        var filtered = entries.Where(entry => kind is null || entry.Kind == kind)
            .Where(entry => string.IsNullOrWhiteSpace(category) || string.Equals(entry.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(search) || entry.Category.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || (entry.Note?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        return new EntryPage(filtered.OrderByDescending(entry => entry.BusinessDate).ThenByDescending(entry => entry.CreatedAt).ThenBy(entry => entry.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count);
    }

    public Task<IReadOnlyList<AssetTransaction>> GetAllAssetTransactionsAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AssetTransaction>>(_assetTransactions.Where(transaction => transaction.LedgerId == ledgerId && !transaction.IsReversed).ToList());
    }

    public Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Ledger is not null);
    }

    public Task<Ledger?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Ledger);
    }

    public Task<Ledger?> GetForUpdateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Ledger);
    }

    public Task AddAsync(Ledger ledger, CashAccount cashAccount, CancellationToken cancellationToken)
    {
        Ledger = ledger;
        CashAccount = cashAccount;
        return Task.CompletedTask;
    }

    public Task<LedgerEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_entries.SingleOrDefault(entry => entry.Id == entryId));
    }

    public Task<LedgerEntry?> FindEntryByRequestIdAsync(
        Guid ledgerId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_entries.SingleOrDefault(
            entry => entry.LedgerId == ledgerId && entry.RequestId == requestId));
    }

    public Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LedgerEntry> entries = _entries
            .Where(entry =>
                entry.LedgerId == ledgerId
                && entry.Status == LedgerEntryStatus.Active
                && entry.BusinessDate >= from
                && entry.BusinessDate <= to)
            .OrderByDescending(entry => entry.BusinessDate)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToList();

        return Task.FromResult(entries);
    }

    public Task AddEntryAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CashAdjustment>> GetCashAdjustmentsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CashAdjustment> adjustments = _adjustments
            .Where(adjustment =>
                adjustment.LedgerId == ledgerId
                && !adjustment.IsReversed
                && adjustment.BusinessDate >= from
                && adjustment.BusinessDate <= to)
            .OrderByDescending(adjustment => adjustment.BusinessDate)
            .ThenByDescending(adjustment => adjustment.CreatedAt)
            .ToList();

        return Task.FromResult(adjustments);
    }

    public Task AddCashAdjustmentAsync(CashAdjustment adjustment, CancellationToken cancellationToken)
    {
        _adjustments.Add(adjustment);
        return Task.CompletedTask;
    }

    public Task<ProvidentFundAccount?> GetProvidentFundAccountAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ProvidentAccount?.LedgerId == ledgerId ? ProvidentAccount : null);
    }

    public Task<ProvidentFundAccount?> GetProvidentFundAccountForUpdateAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ProvidentAccount?.LedgerId == ledgerId ? ProvidentAccount : null);
    }

    public Task AddProvidentFundAccountAsync(
        ProvidentFundAccount account,
        CancellationToken cancellationToken)
    {
        ProvidentAccount = account;
        return Task.CompletedTask;
    }

    public Task<GoldHolding?> GetGoldHoldingAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(GoldHolding?.LedgerId == ledgerId ? GoldHolding : null);
    }

    public Task<GoldHolding?> GetGoldHoldingForUpdateAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GoldHolding?.LedgerId == ledgerId ? GoldHolding : null);
    }

    public Task AddGoldHoldingAsync(GoldHolding holding, CancellationToken cancellationToken)
    {
        GoldHolding = holding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FundPosition>> GetFundPositionsAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FundPosition> positions = _fundPositions
            .Where(position => position.LedgerId == ledgerId)
            .OrderBy(position => position.Name)
            .ThenBy(position => position.Id)
            .ToList();

        return Task.FromResult(positions);
    }

    public Task<FundPosition?> GetFundPositionForUpdateAsync(
        Guid ledgerId,
        Guid positionId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_fundPositions.SingleOrDefault(
            position => position.Id == positionId && position.LedgerId == ledgerId));
    }

    public Task AddFundPositionAsync(FundPosition position, CancellationToken cancellationToken)
    {
        _fundPositions.Add(position);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AssetTransaction>> GetAssetTransactionsAsync(
        Guid ledgerId,
        AssetType assetType,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssetTransaction> transactions = _assetTransactions
            .Where(transaction =>
                transaction.LedgerId == ledgerId
                && !transaction.IsReversed
                && transaction.AssetType == assetType
                && (from is null || transaction.BusinessDate >= from)
                && (to is null || transaction.BusinessDate <= to))
            .OrderByDescending(transaction => transaction.BusinessDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .Take(take)
            .ToList();

        return Task.FromResult(transactions);
    }

    public Task<IReadOnlyList<AssetTransaction>> GetAssetCashTransactionsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssetTransaction> transactions = _assetTransactions
            .Where(transaction =>
                transaction.LedgerId == ledgerId
                && !transaction.IsReversed
                && transaction.CashEffect != 0
                && transaction.BusinessDate >= from
                && transaction.BusinessDate <= to)
            .OrderByDescending(transaction => transaction.BusinessDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ToList();

        return Task.FromResult(transactions);
    }

    public Task AddAssetTransactionAsync(AssetTransaction transaction, CancellationToken cancellationToken)
    {
        _assetTransactions.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<AssetTransaction?> GetAssetTransactionForUpdateAsync(
        Guid ledgerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_assetTransactions.SingleOrDefault(
            transaction => transaction.LedgerId == ledgerId && transaction.Id == transactionId));
    }

    public Task<AssetTransaction?> FindAssetTransactionByRequestIdAsync(
        Guid ledgerId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_assetTransactions.SingleOrDefault(
            transaction => transaction.LedgerId == ledgerId && transaction.RequestId == requestId));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        _savedProvident = ProvidentAccount;
        _savedGold = GoldHolding;
        _savedObjects.Clear();
        _savedObjects.AddRange(_entries.Cast<object>().Concat(_adjustments).Concat(_assetTransactions).Concat(_fundPositions).Concat(_receipts).Concat(_prices));
        _savedObjects.AddRange(new object?[] { Ledger, CashAccount, ProvidentAccount, GoldHolding }.OfType<object>());
        _saved = _savedObjects.Distinct().ToDictionary(item => item, item => item.GetType().GetProperties()
            .Where(property => property.SetMethod is not null && (property.PropertyType.IsValueType || property.PropertyType == typeof(string)))
            .ToDictionary(property => property, property => property.GetValue(item)));
        return Task.CompletedTask;
    }
}
