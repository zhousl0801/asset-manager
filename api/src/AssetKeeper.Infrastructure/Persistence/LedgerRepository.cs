using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AssetKeeper.Infrastructure.Persistence;

public sealed class LedgerRepository : ILedgerRepository
{
    private readonly AppDbContext _dbContext;

    public LedgerRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<HistoryAuditDto>> GetHistoryAuditAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return await _dbContext.MutationReceipts.AsNoTracking().Where(receipt => receipt.LedgerId == ledgerId && receipt.Operation == "history.change")
            .OrderByDescending(receipt => receipt.CreatedAt).Take(100)
            .Select(receipt => new HistoryAuditDto(receipt.RequestId, receipt.CreatedAt, receipt.BeforeJson)).ToListAsync(cancellationToken);
    }

    public async Task<LedgerReplayData> GetReplayDataAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        var ledger = await _dbContext.Ledgers.Include(ledger => ledger.CashAccount).SingleAsync(ledger => ledger.Id == ledgerId, cancellationToken);
        await _dbContext.LedgerEntries.Where(entry => entry.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.CashAdjustments.Where(adjustment => adjustment.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.AssetTransactions.Where(transaction => transaction.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.PriceObservations.Where(price => price.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.FundPositions.Where(position => position.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.ProvidentFundAccounts.Where(account => account.LedgerId == ledgerId).LoadAsync(cancellationToken);
        await _dbContext.GoldHoldings.Where(holding => holding.LedgerId == ledgerId).LoadAsync(cancellationToken);
        return new LedgerReplayData(ledger,
            _dbContext.LedgerEntries.Local.Where(entry => entry.LedgerId == ledgerId).ToList(),
            _dbContext.CashAdjustments.Local.Where(adjustment => adjustment.LedgerId == ledgerId).ToList(),
            _dbContext.AssetTransactions.Local.Where(transaction => transaction.LedgerId == ledgerId).ToList(),
            _dbContext.PriceObservations.Local.Where(price => price.LedgerId == ledgerId).ToList(),
            _dbContext.FundPositions.Local.Where(position => position.LedgerId == ledgerId).ToList(),
            _dbContext.ProvidentFundAccounts.Local.SingleOrDefault(account => account.LedgerId == ledgerId),
            _dbContext.GoldHoldings.Local.SingleOrDefault(holding => holding.LedgerId == ledgerId));
    }

    public async Task AddPriceObservationAsync(PriceObservation price, CancellationToken cancellationToken)
    {
        await _dbContext.PriceObservations.AddAsync(price, cancellationToken);
    }

    public Task<PriceObservation?> GetLatestPriceObservationAsync(
        Guid ledgerId,
        AssetType assetType,
        Guid? positionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.PriceObservations
            .AsNoTracking()
            .Where(price => price.LedgerId == ledgerId && price.AssetType == assetType
                && price.FundPositionId == positionId && !price.IsSuperseded)
            .OrderByDescending(price => price.EffectiveDate)
            .ThenByDescending(price => price.RevisionOrder)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void DiscardChanges() => _dbContext.ChangeTracker.Clear();

    public Task<MutationReceipt?> FindMutationReceiptAsync(Guid ledgerId, Guid requestId, CancellationToken cancellationToken)
    {
        return _dbContext.MutationReceipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.LedgerId == ledgerId && receipt.RequestId == requestId, cancellationToken);
    }

    public async Task AddMutationReceiptAsync(MutationReceipt receipt, CancellationToken cancellationToken)
    {
        await _dbContext.MutationReceipts.AddAsync(receipt, cancellationToken);
    }

    public async Task<EntryTotals> GetEntryTotalsAsync(Guid ledgerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var groups = await _dbContext.LedgerEntries.AsNoTracking()
            .Where(entry => entry.LedgerId == ledgerId && entry.Status == LedgerEntryStatus.Active && entry.BusinessDate >= from && entry.BusinessDate <= to)
            .GroupBy(entry => entry.Kind)
            .Select(group => new { Kind = group.Key, Amount = group.Sum(entry => entry.Amount), Count = group.Count() })
            .ToListAsync(cancellationToken);

        return new EntryTotals(
            groups.Where(group => group.Kind == LedgerEntryKind.Income).Sum(group => group.Amount),
            groups.Where(group => group.Kind == LedgerEntryKind.Expense).Sum(group => group.Amount),
            groups.Sum(group => group.Count));
    }

    public async Task<EntryPage> GetEntryPageAsync(Guid ledgerId, DateOnly from, DateOnly to, LedgerEntryKind? kind, string? category, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.LedgerEntries.AsNoTracking()
            .Where(entry => entry.LedgerId == ledgerId && entry.Status == LedgerEntryStatus.Active && entry.BusinessDate >= from && entry.BusinessDate <= to);

        if (kind is not null)
        {
            query = query.Where(entry => entry.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var value = category.Trim().ToLower();
            query = query.Where(entry => entry.Category.ToLower() == value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(entry => entry.Category.ToLower().Contains(value) || (entry.Note != null && entry.Note.ToLower().Contains(value)));
        }

        var count = await query.CountAsync(cancellationToken);
        var entries = await query.OrderByDescending(entry => entry.BusinessDate).ThenByDescending(entry => entry.CreatedAt).ThenBy(entry => entry.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new EntryPage(entries, count);
    }

    public async Task<IReadOnlyList<AssetTransaction>> GetAllAssetTransactionsAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return await _dbContext.AssetTransactions.AsNoTracking().Where(transaction => transaction.LedgerId == ledgerId && !transaction.IsReversed).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Ledgers.AnyAsync(cancellationToken);
    }

    public Task<Ledger?> GetAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Ledgers
            .AsNoTracking()
            .Include(ledger => ledger.CashAccount)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Ledger?> GetForUpdateAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Ledgers
            .Include(ledger => ledger.CashAccount)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Ledger ledger, CashAccount cashAccount, CancellationToken cancellationToken)
    {
        await _dbContext.Ledgers.AddAsync(ledger, cancellationToken);
        await _dbContext.CashAccounts.AddAsync(cashAccount, cancellationToken);
    }

    public Task<LedgerEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken)
    {
        return _dbContext.LedgerEntries
            .SingleOrDefaultAsync(entry => entry.Id == entryId, cancellationToken);
    }

    public Task<LedgerEntry?> FindEntryByRequestIdAsync(
        Guid ledgerId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return _dbContext.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.LedgerId == ledgerId && entry.RequestId == requestId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.LedgerId == ledgerId
                && entry.Status == LedgerEntryStatus.Active
                && entry.BusinessDate >= from
                && entry.BusinessDate <= to)
            .OrderByDescending(entry => entry.BusinessDate)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddEntryAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        await _dbContext.LedgerEntries.AddAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<EntryCategory>> GetCategoriesAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EntryCategories
            .AsNoTracking()
            .Where(category => category.LedgerId == ledgerId)
            .OrderBy(category => category.Kind)
            .ThenBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<EntryCategory?> GetCategoryAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string name,
        CancellationToken cancellationToken)
    {
        var normalized = EntryCategory.NormalizeName(name);

        return _dbContext.EntryCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                category => category.LedgerId == ledgerId && category.Kind == kind && category.Name == normalized,
                cancellationToken);
    }

    public Task<EntryCategory?> GetCategoryForUpdateAsync(
        Guid ledgerId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return _dbContext.EntryCategories
            .SingleOrDefaultAsync(
                category => category.LedgerId == ledgerId && category.Id == categoryId,
                cancellationToken);
    }

    public Task<EntryCategory?> GetCategoryBySystemKeyAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string systemKey,
        CancellationToken cancellationToken)
    {
        return _dbContext.EntryCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                category => category.LedgerId == ledgerId
                    && category.Kind == kind
                    && category.SystemKey == systemKey,
                cancellationToken);
    }

    public async Task AddCategoryAsync(EntryCategory category, CancellationToken cancellationToken)
    {
        await _dbContext.EntryCategories.AddAsync(category, cancellationToken);
    }

    public async Task AddCategoriesAsync(IReadOnlyList<EntryCategory> categories, CancellationToken cancellationToken)
    {
        await _dbContext.EntryCategories.AddRangeAsync(categories, cancellationToken);
    }

    public void RemoveCategory(EntryCategory category)
    {
        _dbContext.EntryCategories.Remove(category);
    }

    public async Task RenameEntryCategoryAsync(
        Guid ledgerId,
        LedgerEntryKind kind,
        string oldName,
        string newName,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.LedgerEntries
            .Where(entry => entry.LedgerId == ledgerId && entry.Kind == kind && entry.Category == oldName)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            entry.ApplyCategoryRename(newName);
        }
    }

    public async Task<IReadOnlyList<CashAdjustment>> GetCashAdjustmentsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CashAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                adjustment.LedgerId == ledgerId
                && !adjustment.IsReversed
                && adjustment.BusinessDate >= from
                && adjustment.BusinessDate <= to)
            .OrderByDescending(adjustment => adjustment.BusinessDate)
            .ThenByDescending(adjustment => adjustment.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddCashAdjustmentAsync(CashAdjustment adjustment, CancellationToken cancellationToken)
    {
        await _dbContext.CashAdjustments.AddAsync(adjustment, cancellationToken);
    }

    public Task<ProvidentFundAccount?> GetProvidentFundAccountAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return _dbContext.ProvidentFundAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.LedgerId == ledgerId, cancellationToken);
    }

    public Task<ProvidentFundAccount?> GetProvidentFundAccountForUpdateAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return _dbContext.ProvidentFundAccounts
            .SingleOrDefaultAsync(account => account.LedgerId == ledgerId, cancellationToken);
    }

    public async Task AddProvidentFundAccountAsync(
        ProvidentFundAccount account,
        CancellationToken cancellationToken)
    {
        await _dbContext.ProvidentFundAccounts.AddAsync(account, cancellationToken);
    }

    public Task<GoldHolding?> GetGoldHoldingAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return _dbContext.GoldHoldings
            .AsNoTracking()
            .SingleOrDefaultAsync(holding => holding.LedgerId == ledgerId, cancellationToken);
    }

    public Task<GoldHolding?> GetGoldHoldingForUpdateAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return _dbContext.GoldHoldings
            .SingleOrDefaultAsync(holding => holding.LedgerId == ledgerId, cancellationToken);
    }

    public async Task AddGoldHoldingAsync(GoldHolding holding, CancellationToken cancellationToken)
    {
        await _dbContext.GoldHoldings.AddAsync(holding, cancellationToken);
    }

    public async Task<IReadOnlyList<FundGroup>> GetFundGroupsAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.FundGroups
            .AsNoTracking()
            .Where(group => group.LedgerId == ledgerId)
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<FundGroup?> GetFundGroupForUpdateAsync(
        Guid ledgerId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        return _dbContext.FundGroups
            .SingleOrDefaultAsync(
                group => group.Id == groupId && group.LedgerId == ledgerId,
                cancellationToken);
    }

    public async Task AddFundGroupAsync(FundGroup group, CancellationToken cancellationToken)
    {
        await _dbContext.FundGroups.AddAsync(group, cancellationToken);
    }

    public void RemoveFundGroup(FundGroup group)
    {
        _dbContext.FundGroups.Remove(group);
    }

    public async Task<IReadOnlyList<FundPosition>> GetFundPositionsAsync(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.FundPositions
            .AsNoTracking()
            .Where(position => position.LedgerId == ledgerId)
            .OrderBy(position => position.Name)
            .ThenBy(position => position.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<FundPosition?> GetFundPositionForUpdateAsync(
        Guid ledgerId,
        Guid positionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.FundPositions
            .SingleOrDefaultAsync(
                position => position.Id == positionId && position.LedgerId == ledgerId,
                cancellationToken);
    }

    public async Task AddFundPositionAsync(FundPosition position, CancellationToken cancellationToken)
    {
        await _dbContext.FundPositions.AddAsync(position, cancellationToken);
    }

    public async Task<IReadOnlyList<AssetTransaction>> GetAssetTransactionsAsync(
        Guid ledgerId,
        AssetType assetType,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AssetTransactions
            .AsNoTracking()
            .Where(transaction => transaction.LedgerId == ledgerId && !transaction.IsReversed && transaction.AssetType == assetType);

        if (from is not null)
        {
            query = query.Where(transaction => transaction.BusinessDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(transaction => transaction.BusinessDate <= to);
        }

        return await query
            .OrderByDescending(transaction => transaction.BusinessDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssetTransaction>> GetAssetCashTransactionsAsync(
        Guid ledgerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AssetTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.LedgerId == ledgerId
                && !transaction.IsReversed
                && transaction.CashEffect != 0
                && transaction.BusinessDate >= from
                && transaction.BusinessDate <= to)
            .OrderByDescending(transaction => transaction.BusinessDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAssetTransactionAsync(
        AssetTransaction transaction,
        CancellationToken cancellationToken)
    {
        await _dbContext.AssetTransactions.AddAsync(transaction, cancellationToken);
    }

    public Task<AssetTransaction?> GetAssetTransactionForUpdateAsync(
        Guid ledgerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.AssetTransactions
            .SingleOrDefaultAsync(
                transaction => transaction.Id == transactionId && transaction.LedgerId == ledgerId,
                cancellationToken);
    }

    public Task<AssetTransaction?> FindAssetTransactionByRequestIdAsync(
        Guid ledgerId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return _dbContext.AssetTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                transaction => transaction.LedgerId == ledgerId && transaction.RequestId == requestId,
                cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConcurrencyConflictException(exception);
        }
    }
}
