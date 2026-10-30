using System.Globalization;
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

        // 月初基线落在账本起始日之前，才能做真正的“同口径”月度比较。
        var hasPreMonthBaseline = ledger.StartDate < monthStart;
        var baselineDate = hasPreMonthBaseline ? monthStart.AddDays(-1) : ledger.StartDate;
        var before = calculator.Calculate(data, facts, baselineDate, currentDate: today);
        var now = calculator.Calculate(data, facts, today, currentDate: today);

        var beforeValues = ReadValues(before, ledger);
        var nowValues = ReadValues(now, ledger);

        var comparableTypes = beforeValues
            .Where(pair => pair.Value.Present && nowValues[pair.Key].Present)
            .ToList();
        var commonKnown = comparableTypes.Count > 0
            && comparableTypes.All(pair => pair.Value.Value is not null && nowValues[pair.Key].Value is not null);
        var comparableChange = hasPreMonthBaseline && commonKnown
            ? Round(comparableTypes.Sum(pair => nowValues[pair.Key].Value!.Value - pair.Value.Value!.Value))
            : (decimal?)null;
        var comparableBase = hasPreMonthBaseline && commonKnown
            ? Round(comparableTypes.Sum(pair => pair.Value.Value!.Value))
            : (decimal?)null;

        var addedTypes = nowValues
            .Where(pair => !beforeValues[pair.Key].Present && pair.Value.Present && pair.Value.Value is not null)
            .ToList();
        var addedRecords = addedTypes.Count > 0
            ? Round(addedTypes.Sum(pair => pair.Value.Value!.Value))
            : 0m;

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

        decimal? netDeposit = null;
        decimal? marketValueChange = null;

        if (comparableChange is not null)
        {
            var providentComparable = beforeValues["providentFund"].Present
                && nowValues["providentFund"].Present;
            netDeposit = Round(livingSurplus
                + (providentComparable ? providentDeposit : 0m)
                + reconciliationDifference);
            marketValueChange = Round(comparableChange.Value - netDeposit.Value);
        }

        var assetChanges = new List<AssetChangeDto>
        {
            BuildAssetChange("cash", beforeValues, nowValues),
            BuildAssetChange("providentFund", beforeValues, nowValues),
            BuildAssetChange("fund", beforeValues, nowValues),
            BuildAssetChange("gold", beforeValues, nowValues),
            BuildAssetChange("stock", beforeValues, nowValues)
        };

        if (!hasPreMonthBaseline)
        {
            notes.Add("账本起始日晚于本月月初，本月暂无可比基线；新增记录单独列出。");
        }
        else if (comparableChange is null)
        {
            notes.Add("部分日期资产或行情依据不足，无法计算同口径变化。");
            notes.AddRange(now.Issues.Distinct());
        }

        if (addedRecords > 0)
        {
            notes.Add($"含新增记录 {addedRecords.ToString("N2", CultureInfo.InvariantCulture)} 元，不计入本月变化。");
        }

        return new MonthChangeDto(
            from,
            today,
            comparableChange,
            livingSurplus,
            providentDeposit,
            marketValueChange,
            reconciliationDifference,
            comparableChange is not null,
            notes.Distinct().ToList(),
            assetChanges,
            comparableChange,
            comparableBase,
            addedRecords,
            marketValueChange,
            netDeposit,
            comparableChange is not null);
    }

    private static Dictionary<string, AssetValue> ReadValues(
        ReplayResult result,
        AssetKeeper.Domain.Ledgers.Ledger ledger)
    {
        var values = new Dictionary<string, AssetValue>
        {
            ["cash"] = new(result.CashBalance is not null, result.CashBalance),
            ["providentFund"] = ledger.ProvidentFundNotHeld && result.ProvidentBalance is null
                ? new AssetValue(true, 0m)
                : new AssetValue(result.ProvidentBalance is not null, result.ProvidentBalance),
            ["fund"] = ReadFund(result, ledger),
            ["gold"] = ReadGold(result, ledger),
            ["stock"] = ReadStock(result, ledger)
        };

        return values;
    }

    private static AssetValue ReadFund(ReplayResult result, AssetKeeper.Domain.Ledgers.Ledger ledger)
    {
        var recorded = result.Funds
            .Where(position => position.CoverageFrom is not null && position.CoverageFrom <= result.AsOfDate)
            .ToList();

        if (recorded.Count == 0)
        {
            return ledger.FundNotHeld ? new AssetValue(true, 0m) : new AssetValue(false, null);
        }

        if (recorded.Any(position => position.Value is null))
        {
            return new AssetValue(true, null);
        }

        return new AssetValue(true, recorded.Sum(position =>
            (position.Value ?? 0m) + position.PendingAmount + position.PendingArrivalAmount));
    }

    private static AssetValue ReadGold(ReplayResult result, AssetKeeper.Domain.Ledgers.Ledger ledger)
    {
        if (result.Gold?.CoverageFrom is not DateOnly goldFrom || goldFrom > result.AsOfDate)
        {
            return ledger.GoldNotHeld ? new AssetValue(true, 0m) : new AssetValue(false, null);
        }

        return new AssetValue(true, result.Gold.Value is null
            ? null
            : result.Gold.Value + result.Gold.PendingArrivalAmount);
    }

    private static AssetValue ReadStock(ReplayResult result, AssetKeeper.Domain.Ledgers.Ledger ledger)
    {
        var recorded = result.Stocks
            .Where(position => position.CoverageFrom is not null && position.CoverageFrom <= result.AsOfDate)
            .ToList();

        if (recorded.Count == 0)
        {
            return ledger.StockNotHeld ? new AssetValue(true, 0m) : new AssetValue(false, null);
        }

        if (recorded.Any(position => position.Value is null))
        {
            return new AssetValue(true, null);
        }

        return new AssetValue(true, recorded.Sum(position => position.Value ?? 0m));
    }

    private static AssetChangeDto BuildAssetChange(
        string assetType,
        Dictionary<string, AssetValue> before,
        Dictionary<string, AssetValue> now)
    {
        var had = before[assetType].Present;
        var has = now[assetType].Present;

        if (!had && has)
        {
            return new AssetChangeDto(assetType, null, true);
        }

        if (!had || !has || before[assetType].Value is null || now[assetType].Value is null)
        {
            return new AssetChangeDto(assetType, null, false);
        }

        return new AssetChangeDto(assetType, Round(now[assetType].Value!.Value - before[assetType].Value!.Value), false);
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
                && fact.AssetType is AssetType.Fund or AssetType.Gold or AssetType.Stock
                && fact.Kind is AssetTransactionKind.FundCorrection or AssetTransactionKind.GoldCorrection
                    or AssetTransactionKind.StockCorrection)
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
                previousQuantity = correction.AssetType switch
                {
                    AssetType.Fund => earlier.Funds.FirstOrDefault(item => item.PositionId == correction.PositionId)?.Quantity ?? 0m,
                    AssetType.Stock => earlier.Stocks.FirstOrDefault(item => item.PositionId == correction.PositionId)?.Quantity ?? 0m,
                    _ => earlier.Gold?.Quantity ?? 0m
                };
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

    private sealed record AssetValue(bool Present, decimal? Value);
}
