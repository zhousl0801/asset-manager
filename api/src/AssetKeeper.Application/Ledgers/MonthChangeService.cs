using AssetKeeper.Application.History;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Ledgers;

public sealed class MonthChangeService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public MonthChangeService(ILedgerRepository ledgerRepository, BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<MonthChangeDto?> GetAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var today = _dates.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var from = ledger.StartDate > monthStart ? ledger.StartDate : monthStart;
        var notes = new List<string>();
        var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var facts = BusinessFact.From(data);
        var calculator = new LedgerReplayCalculator();

        var baseline = monthStart.AddDays(-1);
        var before = calculator.Calculate(data, facts, baseline, currentDate: today);

        if (before.TotalAssets is null && ledger.StartDate > baseline)
        {
            baseline = ledger.StartDate;
            before = calculator.Calculate(data, facts, baseline, currentDate: today);
            notes.Add("本月变化自账本起始日起算。");
        }

        var now = calculator.Calculate(data, facts, today, currentDate: today);
        var totals = await _ledgerRepository.GetEntryTotalsAsync(ledger.Id, from, today, cancellationToken);
        var livingSurplus = Round(totals.Income - totals.Expense);
        var providentDeposit = Round(facts
            .Where(fact => !fact.IsReversed
                && fact.AssetType == AssetType.ProvidentFund
                && fact.Kind == AssetTransactionKind.ProvidentDeposit
                && fact.Date >= from
                && fact.Date <= today)
            .Sum(fact => fact.Amount));
        var cashDifference = Round(data.Adjustments
            .Where(adjustment => !adjustment.IsReversed
                && adjustment.BusinessDate >= from
                && adjustment.BusinessDate <= today)
            .Sum(adjustment => adjustment.Difference));
        var correctionDifference = CalculateCorrectionDifference(data, facts, calculator, from, today, notes);
        var reconciliationDifference = Round(cashDifference + correctionDifference);
        var totalChange = before.TotalAssets is null || now.TotalAssets is null
            ? (decimal?)null
            : Round(now.TotalAssets.Value - before.TotalAssets.Value);

        if (totalChange is null)
        {
            notes.Add("部分日期资产或行情依据不足，无法计算本月变化。");
            notes.AddRange(now.Issues.Distinct());
        }

        var investmentProfit = totalChange is null
            ? (decimal?)null
            : Round(totalChange.Value - livingSurplus - providentDeposit - reconciliationDifference);

        return new MonthChangeDto(
            from,
            today,
            totalChange,
            livingSurplus,
            providentDeposit,
            investmentProfit,
            reconciliationDifference,
            totalChange is not null,
            notes.Distinct().ToList());
    }

    private static decimal CalculateCorrectionDifference(
        LedgerReplayData data,
        IReadOnlyList<BusinessFact> facts,
        LedgerReplayCalculator calculator,
        DateOnly from,
        DateOnly to,
        List<string> notes)
    {
        var corrections = facts
            .Where(fact => !fact.IsReversed
                && fact.Date >= from
                && fact.Date <= to
                && fact.AssetType is AssetType.Fund or AssetType.Gold
                && fact.Kind is AssetTransactionKind.FundCorrection or AssetTransactionKind.GoldCorrection)
            .ToList();
        var difference = 0m;

        foreach (var correction in corrections)
        {
            if (correction.Price is null)
            {
                notes.Add("持仓核对差额缺少当日行情，已计入投资盈亏。");
                continue;
            }

            decimal previousQuantity;

            try
            {
                var earlierFacts = facts
                    .Where(fact => fact.Date < correction.Date
                        || (fact.Date == correction.Date && fact.Sequence < correction.Sequence))
                    .ToList();
                var earlier = calculator.Calculate(data, earlierFacts, correction.Date, currentDate: correction.Date);
                previousQuantity = correction.AssetType == AssetType.Fund
                    ? earlier.Funds.FirstOrDefault(item => item.PositionId == correction.PositionId)?.Quantity ?? 0m
                    : earlier.Gold?.Quantity ?? 0m;
            }
            catch (HistoryValidationException)
            {
                notes.Add("持仓核对前序业务依据不足，核对差额已计入投资盈亏。");
                continue;
            }

            difference += ((correction.Quantity ?? 0m) - previousQuantity) * correction.Price.Value;
        }

        return Round(difference);
    }

    private static decimal Round(decimal value)
    {
        return decimal.Round(value, 2);
    }
}
