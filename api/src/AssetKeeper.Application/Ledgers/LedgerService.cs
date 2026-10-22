using System.Globalization;
using AssetKeeper.Application.History;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed class LedgerService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public LedgerService(ILedgerRepository ledgerRepository, BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<LedgerOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        var (from, to) = EntryPeriodRange.For(EntryPeriod.Month, _businessDateProvider.Today);
        var totals = await _ledgerRepository.GetEntryTotalsAsync(ledger.Id, from, to, cancellationToken);
        var providentAccount = await _ledgerRepository.GetProvidentFundAccountAsync(
            ledger.Id,
            cancellationToken);
        var fundPositions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var goldHolding = await _ledgerRepository.GetGoldHoldingAsync(ledger.Id, cancellationToken);
        var providentTransactions = providentAccount is null
            ? Array.Empty<AssetTransaction>()
            : await _ledgerRepository.GetAssetTransactionsAsync(
                ledger.Id,
                AssetType.ProvidentFund,
                from,
                to,
                int.MaxValue,
                cancellationToken);
        var monthDeposit = providentTransactions
            .Where(transaction => transaction.Kind == AssetTransactionKind.ProvidentDeposit)
            .Sum(transaction => transaction.Amount);
        var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var snapshot = new LedgerReplayCalculator().Calculate(data, BusinessFact.From(data), _businessDateProvider.Today, currentDate: _businessDateProvider.Today);
        var pendingFund = snapshot.Funds.Sum(item => item.PendingAmount);
        var pendingArrival = snapshot.Funds.Sum(item => item.PendingArrivalAmount);
        var pendingGoldArrival = snapshot.Gold?.PendingArrivalAmount ?? 0m;
        var fundNote = $"持有 {fundPositions.Count} 只基金";

        if (pendingFund > 0)
        {
            fundNote += $" · 待确认 {pendingFund.ToString("N2", CultureInfo.InvariantCulture)}";
        }

        if (pendingArrival > 0)
        {
            fundNote += $" · 待到账 {pendingArrival.ToString("N2", CultureInfo.InvariantCulture)}";
        }

        var notConfigured = new AssetOverviewDto(false, null, "尚未录入");
        var fund = fundPositions.Count == 0
            ? notConfigured
            : new AssetOverviewDto(
                true,
                fundPositions.Sum(position => position.Value) + pendingFund + pendingArrival,
                fundNote);
        var provident = providentAccount is null
            ? notConfigured
            : new AssetOverviewDto(
                true,
                providentAccount.Balance,
                $"本月缴存 +{monthDeposit.ToString("N2", CultureInfo.InvariantCulture)}");
        var goldNote = goldHolding is null ? string.Empty : $"持有 {goldHolding.Grams:0.####} 克";

        if (pendingGoldArrival > 0)
        {
            goldNote += $" · 待到账 {pendingGoldArrival.ToString("N2", CultureInfo.InvariantCulture)}";
        }

        var gold = goldHolding is null
            ? notConfigured
            : new AssetOverviewDto(
                true,
                goldHolding.Value + pendingGoldArrival,
                goldNote);

        if (fundPositions.Count > 0 && snapshot.Funds.Any(item => item.Value is null))
        {
            fund = fund with { Amount = null, Note = "行情或起始依据不足，前往历史核对" };
        }
        else if (snapshot.Funds.Any(item => !item.CanRebuild))
        {
            fund = fund with { Note = "当前缓存估值，持仓日期待确认" };
        }

        if (goldHolding is not null && snapshot.Gold?.Value is null)
        {
            gold = gold with { Amount = null, Note = "行情或起始依据不足，前往历史核对" };
        }
        else if (snapshot.Gold is { CanRebuild: false })
        {
            gold = gold with { Note = "当前缓存估值，持仓日期待确认" };
        }

        return Map(ledger, ledger.CashAccount, totals.Income, totals.Expense, totals.Count > 0, fund, provident, gold)
            with
        { IsComplete = snapshot.TotalAssets is not null };
    }

    public async Task<InitializeLedgerResult> InitializeAsync(InitializeLedgerRequest request, CancellationToken cancellationToken)
    {
        var now = _businessDateProvider.UtcNow;
        var businessToday = _businessDateProvider.Today;
        var errors = Validate(request, businessToday);

        if (errors.Count > 0)
        {
            return InitializeLedgerResult.Invalid(errors);
        }

        if (await _ledgerRepository.ExistsAsync(cancellationToken))
        {
            return InitializeLedgerResult.AlreadyExists();
        }

        var ledger = Ledger.Create(request.StartDate, businessToday, now);
        var cashAccount = CashAccount.Create(ledger.Id, request.CashBalance, request.CashAsOfDate ?? request.StartDate, now);
        ledger.SetCashAccount(cashAccount);

        await _ledgerRepository.AddAsync(ledger, cashAccount, cancellationToken);
        await _ledgerRepository.AddCategoriesAsync(
            EntryCategoryDefaults.CreateFor(ledger.Id, now).ToList(),
            cancellationToken);

        try
        {
            await _ledgerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return InitializeLedgerResult.AlreadyExists();
        }

        return InitializeLedgerResult.Created(Map(
            ledger,
            cashAccount,
            0m,
            0m,
            false,
            new AssetOverviewDto(false, null, "尚未录入"),
            new AssetOverviewDto(false, null, "尚未录入"),
            new AssetOverviewDto(false, null, "尚未录入")));
    }

    private static List<string> Validate(InitializeLedgerRequest request, DateOnly businessToday)
    {
        var errors = new List<string>();

        if (request.StartDate == default)
        {
            errors.Add("起始日期无效。");
        }
        else if (request.StartDate > businessToday)
        {
            errors.Add("起始日期不能晚于今天。");
        }

        if (request.CashAsOfDate is DateOnly cashDate && (cashDate < request.StartDate || cashDate > businessToday))
        {
            errors.Add("起始现金余额日期须在账本起始日期与今天之间。");
        }

        if (request.CashBalance < 0)
        {
            errors.Add("现金余额不能为负数。");
        }
        else if (decimal.Round(request.CashBalance, 2) != request.CashBalance)
        {
            errors.Add("现金余额最多保留两位小数。");
        }
        else if (request.CashBalance > CashAccount.MaximumBalance)
        {
            errors.Add("现金余额超出允许范围。");
        }

        return errors;
    }

    private static LedgerOverviewDto Map(
        Ledger ledger,
        CashAccount cashAccount,
        decimal income,
        decimal expense,
        bool hasRecords,
        AssetOverviewDto fund,
        AssetOverviewDto providentFund,
        AssetOverviewDto gold)
    {
        var cash = new AssetOverviewDto(true, cashAccount.Balance, "统一余额");
        var totalAssets = cashAccount.Balance
            + (fund.Amount ?? 0m)
            + (providentFund.Amount ?? 0m)
            + (gold.Amount ?? 0m);

        return new LedgerOverviewDto(
            ledger.Id,
            ledger.Currency,
            ledger.TimeZone,
            ledger.StartDate,
            cashAccount.AsOfDate,
            totalAssets,
            cash,
            fund,
            providentFund,
            gold,
            new MonthSummaryDto(income, expense, income - expense, hasRecords));
    }
}
