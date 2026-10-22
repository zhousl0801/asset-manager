using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Funds;

public sealed class FundReturnService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public FundReturnService(ILedgerRepository ledgerRepository, BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<FundReturnDto?> GetAsync(
        TrendRange range,
        DateOnly? anchor,
        Guid? groupId,
        bool ungrouped,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var today = _dates.Today;
        var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var facts = BusinessFact.From(data);
        var active = facts.Where(fact => !fact.IsReversed).ToList();
        var activeIds = active.Select(fact => fact.Id).ToHashSet();
        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);
        var notes = new List<string>();

        if (groupId is not null && groups.All(group => group.Id != groupId.Value))
        {
            return EmptyResult(range, today, today, ["分组不存在或已删除。"]);
        }

        var positions = ScopePositions(data.Positions, groupId, ungrouped);

        if (positions.Count == 0)
        {
            return EmptyResult(range, today, today, ["当前分组没有基金持仓。"]);
        }

        var fundNames = positions
            .GroupBy(position => position.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var fundNameById = positions.ToDictionary(position => position.Id, position => position.Name);
        var groupNames = groups.ToDictionary(group => group.Id, group => group.Name);
        var anchorDate = anchor ?? today;

        if (anchorDate > today)
        {
            anchorDate = today;
        }

        if (anchorDate < ledger.StartDate)
        {
            anchorDate = ledger.StartDate;
        }

        var from = RangeStart(range, anchorDate, ledger.StartDate);

        if (from > today)
        {
            from = today;
        }

        var calculator = new LedgerReplayCalculator();
        var replays = new Dictionary<DateOnly, ReplayResult>();

        ReplayResult Replay(DateOnly date)
        {
            if (!replays.TryGetValue(date, out var result))
            {
                result = calculator.Calculate(data, facts, date, currentDate: today);
                replays[date] = result;
            }

            return result;
        }

        var coverage = positions.ToDictionary(
            position => position.Id,
            position => Replay(today).Funds.FirstOrDefault(fund => fund.PositionId == position.Id)?.CoverageFrom);
        var baseline = from.AddDays(-1);
        var intervalFrom = from;
        var usesLedgerStart = baseline < ledger.StartDate;

        if (usesLedgerStart)
        {
            baseline = from;
        }

        if (coverage.Values.Any(value => value is null))
        {
            notes.Add("部分基金缺少可靠持仓日期，区间盈亏不计算。");
        }
        else
        {
            var latestCoverage = coverage.Values.Max(value => value!.Value);

            if (latestCoverage > baseline)
            {
                intervalFrom = latestCoverage > from ? latestCoverage : from;
                baseline = intervalFrom;
                usesLedgerStart = false;
                notes.Add($"区间盈亏自 {intervalFrom:yyyy-MM-dd} 起算，此前部分基金尚无持仓依据。");
            }
        }

        if (usesLedgerStart)
        {
            notes.Add("区间盈亏自账本起始日收盘起算。");
        }

        var observations = positions.ToDictionary(
            position => position.Id,
            position => BuildObservations(data, activeIds, ledger.StartDate, today, position.Id));
        var (corrections, correctionsKnown) = BuildCorrections(data, facts, calculator, positions, today);
        var profits = CalculateProfitTable(
            positions,
            observations,
            active,
            Replay,
            corrections,
            correctionsKnown,
            from,
            today);
        var observedByDate = positions
            .SelectMany(position => observations[position.Id]
                .Where(observation => observation.Date >= from && observation.Date <= today)
                .Select(observation => new { PositionId = position.Id, observation.Date }))
            .GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PositionId).ToHashSet());
        var points = new List<FundReturnPointDto>();
        decimal? running = null;
        var totalFunds = fundNames.Count;
        var totalPositions = positions.Count;
        var disclosureDays = 0;

        for (var date = from; date <= today; date = date.AddDays(1))
        {
            var replay = Replay(date);
            var fundValues = positions
                .Select(position => replay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id))
                .Select(TotalValue)
                .ToList();
            var fundValue = fundValues.Any(value => value is null)
                ? (decimal?)null
                : Round(fundValues.Sum(value => value!.Value));
            var observedPositions = observedByDate.GetValueOrDefault(date) ?? [];
            var hasDisclosure = observedPositions.Count > 0;
            var dailyProfits = observedPositions
                .Select(positionId => profits.GetValueOrDefault((positionId, date)))
                .Where(profit => profit is not null)
                .Select(profit => profit!.Value)
                .ToList();
            var dailyProfit = dailyProfits.Count > 0 ? Round(dailyProfits.Sum()) : (decimal?)null;

            if (dailyProfit is not null)
            {
                running = (running ?? 0m) + dailyProfit.Value;
            }

            if (hasDisclosure)
            {
                disclosureDays++;
            }

            var coveredPositions = observedPositions.Count(positionId => profits.GetValueOrDefault((positionId, date)) is not null);
            var disclosedFunds = observedPositions
                .Select(positionId => fundNameById[positionId])
                .Distinct(StringComparer.Ordinal)
                .Count();
            var isComplete = coveredPositions == totalPositions;
            var status = !hasDisclosure
                ? "notDisclosed"
                : isComplete
                    ? "complete"
                    : coveredPositions > 0 ? "partial" : "insufficient";
            points.Add(new FundReturnPointDto(
                date,
                fundValue,
                dailyProfit,
                running,
                disclosedFunds,
                totalFunds,
                coveredPositions,
                totalPositions,
                hasDisclosure,
                isComplete,
                status));
        }

        var closing = Replay(today);
        var opening = baseline <= today ? Replay(baseline) : null;
        var openingValue = opening is null ? null : ScopeValue(opening, positions);
        var closingValue = ScopeValue(closing, positions);
        var scopedIds = positions.Select(position => position.Id).ToHashSet();
        var netTransfer = Round(active
            .Where(fact => fact.AssetType == AssetType.Fund
                && fact.PositionId is not null
                && scopedIds.Contains(fact.PositionId.Value)
                && fact.Date > baseline
                && fact.Date <= today)
            .Sum(fact => -fact.CashEffect));
        var reconciliationDifference = correctionsKnown
            ? Round(corrections
                .Where(item => scopedIds.Contains(item.Key.PositionId)
                    && item.Key.Date > baseline
                    && item.Key.Date <= today)
                .Sum(item => item.Value))
            : (decimal?)null;
        decimal? intervalProfit = null;
        decimal? intervalRate = null;

        if (openingValue is not null && closingValue is not null && reconciliationDifference is not null)
        {
            intervalProfit = Round(closingValue.Value - openingValue.Value - netTransfer - reconciliationDifference.Value);
            intervalRate = openingValue.Value > 0m
                ? Round(intervalProfit.Value / openingValue.Value * 100m)
                : null;
        }
        else if (reconciliationDifference is null)
        {
            notes.Add("部分持仓核对缺少当日行情，区间盈亏不计算。");
        }

        var holding = CalculateHolding(closing, positions);
        var fundSummaries = BuildFundSummaries(
            positions,
            fundNames,
            observations,
            profits,
            active,
            Replay,
            corrections,
            correctionsKnown,
            baseline,
            from,
            today,
            groupNames);

        if (points.Any(point => !point.IsComplete && point.HasDisclosure))
        {
            notes.Add("部分净值日仅有部分基金或份额类别披露，合计只包含已披露项目。");
        }

        if (points.Any(point => !point.HasDisclosure))
        {
            notes.Add("未披露净值的日期不计当日收益，累计收益沿用上一个有效值。");
        }

        return new FundReturnDto(
            range,
            from,
            today,
            intervalFrom,
            openingValue,
            closingValue,
            netTransfer,
            reconciliationDifference,
            intervalProfit,
            intervalRate,
            holding.Profit,
            holding.Rate,
            disclosureDays,
            intervalProfit is not null,
            points,
            fundSummaries,
            notes.Distinct().ToList());
    }

    public async Task<FundReturnDayDto?> GetDayAsync(
        DateOnly date,
        Guid? groupId,
        bool ungrouped,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var today = _dates.Today;
        var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var facts = BusinessFact.From(data);
        var active = facts.Where(fact => !fact.IsReversed).ToList();
        var activeIds = active.Select(fact => fact.Id).ToHashSet();
        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);

        if (groupId is not null && groups.All(group => group.Id != groupId.Value))
        {
            return new FundReturnDayDto(date, null, 0, 0, 0, 0, false, "insufficient", [], ["分组不存在或已删除。"]);
        }

        if (date < ledger.StartDate || date > today)
        {
            return new FundReturnDayDto(date, null, 0, 0, 0, 0, false, "insufficient", [], ["日期不在账本范围内。"]);
        }

        var positions = ScopePositions(data.Positions, groupId, ungrouped);

        if (positions.Count == 0)
        {
            return new FundReturnDayDto(date, null, 0, 0, 0, 0, false, "insufficient", [], ["当前分组没有基金持仓。"]);
        }

        var calculator = new LedgerReplayCalculator();
        var replays = new Dictionary<DateOnly, ReplayResult>();

        ReplayResult Replay(DateOnly target)
        {
            if (!replays.TryGetValue(target, out var result))
            {
                result = calculator.Calculate(data, facts, target, currentDate: today);
                replays[target] = result;
            }

            return result;
        }

        var observations = positions.ToDictionary(
            position => position.Id,
            position => BuildObservations(data, activeIds, ledger.StartDate, date, position.Id));
        var (corrections, correctionsKnown) = BuildCorrections(data, facts, calculator, positions, date);
        var replay = Replay(date);
        var rows = new List<FundReturnDayRowDto>();
        var notes = new List<string>();

        foreach (var position in positions)
        {
            var series = observations[position.Id];
            var observation = series.LastOrDefault(item => item.Date == date);
            var previous = series.LastOrDefault(item => item.Date < date);
            var state = replay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id);
            var value = TotalValue(state);
            decimal? profit = null;
            string? note = null;
            var hasDisclosure = observation is not null;

            if (observation is null)
            {
                note = "该日未披露净值。";
            }
            else if (previous is null)
            {
                note = "缺少前一净值日，无法计算当日收益。";
            }
            else
            {
                profit = CalculatePositionProfit(
                    position.Id,
                    previous.Date,
                    date,
                    active,
                    Replay,
                    corrections,
                    correctionsKnown);

                if (profit is null)
                {
                    note = "该日行情或持仓依据不足。";
                }
            }

            rows.Add(new FundReturnDayRowDto(
                position.Id,
                position.Name,
                position.ShareClass,
                position.Platform,
                value,
                profit,
                state?.Price,
                state?.PriceDate,
                hasDisclosure,
                profit is not null,
                !hasDisclosure ? "notDisclosed" : profit is not null ? "complete" : "insufficient",
                note));
        }

        var totalProfit = rows.Any(row => row.DailyProfit is not null)
            ? Round(rows.Where(row => row.DailyProfit is not null).Sum(row => row.DailyProfit!.Value))
            : (decimal?)null;
        var coveredPositions = rows.Count(row => row.DailyProfit is not null);
        var disclosedFunds = rows
            .Where(row => row.HasDisclosure)
            .Select(row => row.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var totalFunds = rows.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count();
        var isComplete = coveredPositions == rows.Count;

        if (disclosedFunds == 0)
        {
            notes.Add("该日没有基金披露净值。");
        }
        else if (!isComplete)
        {
            notes.Add("该日仅有部分基金或份额类别披露，合计只包含已披露项目。");
        }

        var status = disclosedFunds == 0
            ? "notDisclosed"
            : isComplete
                ? "complete"
                : coveredPositions > 0 ? "partial" : "insufficient";

        return new FundReturnDayDto(
            date,
            totalProfit,
            disclosedFunds,
            totalFunds,
            coveredPositions,
            rows.Count,
            isComplete,
            status,
            rows,
            notes);
    }

    private static IReadOnlyList<FundPosition> ScopePositions(
        IReadOnlyList<FundPosition> positions,
        Guid? groupId,
        bool ungrouped)
    {
        if (groupId is not null)
        {
            return positions.Where(position => position.FundGroupId == groupId.Value).ToList();
        }

        return ungrouped
            ? positions.Where(position => position.FundGroupId is null).ToList()
            : positions.ToList();
    }

    private static DateOnly RangeStart(TrendRange range, DateOnly anchor, DateOnly ledgerStart)
    {
        var from = range switch
        {
            TrendRange.Month => new DateOnly(anchor.Year, anchor.Month, 1),
            TrendRange.Quarter => anchor.AddMonths(-3).AddDays(1),
            TrendRange.Year => new DateOnly(anchor.Year, 1, 1),
            _ => ledgerStart
        };

        return from < ledgerStart ? ledgerStart : from;
    }

    private static IReadOnlyList<Observation> BuildObservations(
        LedgerReplayData data,
        IReadOnlySet<Guid> activeIds,
        DateOnly ledgerStart,
        DateOnly to,
        Guid positionId)
    {
        return data.Prices
            .Where(price => !price.IsSuperseded
                && price.AssetType == AssetType.Fund
                && price.FundPositionId == positionId
                && price.EffectiveDate >= ledgerStart
                && price.EffectiveDate <= to
                && (price.SourceTransactionId is null || activeIds.Contains(price.SourceTransactionId.Value)))
            .GroupBy(price => price.EffectiveDate)
            .Select(group => group.OrderByDescending(price => price.RevisionOrder).First())
            .OrderBy(price => price.EffectiveDate)
            .Select(price => new Observation(price.EffectiveDate, price.Price))
            .ToList();
    }

    private static (Dictionary<(Guid PositionId, DateOnly Date), decimal> Values, bool IsKnown) BuildCorrections(
        LedgerReplayData data,
        IReadOnlyList<BusinessFact> facts,
        LedgerReplayCalculator calculator,
        IReadOnlyList<FundPosition> positions,
        DateOnly to)
    {
        var positionIds = positions.Select(position => position.Id).ToHashSet();
        var values = new Dictionary<(Guid PositionId, DateOnly Date), decimal>();
        var isKnown = true;

        foreach (var correction in facts.Where(fact => !fact.IsReversed
            && fact.Kind == AssetTransactionKind.FundCorrection
            && fact.PositionId is not null
            && positionIds.Contains(fact.PositionId.Value)
            && fact.Date <= to))
        {
            if (correction.Price is null)
            {
                isKnown = false;
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
                previousQuantity = earlier.Funds
                    .FirstOrDefault(fund => fund.PositionId == correction.PositionId)?.Quantity ?? 0m;
            }
            catch (HistoryValidationException)
            {
                isKnown = false;
                continue;
            }

            var key = (correction.PositionId!.Value, correction.Date);
            values[key] = values.GetValueOrDefault(key)
                + ((correction.Quantity ?? 0m) - previousQuantity) * correction.Price.Value;
        }

        return (values, isKnown);
    }

    private static Dictionary<(Guid PositionId, DateOnly Date), decimal?> CalculateProfitTable(
        IReadOnlyList<FundPosition> positions,
        IReadOnlyDictionary<Guid, IReadOnlyList<Observation>> observations,
        IReadOnlyList<BusinessFact> active,
        Func<DateOnly, ReplayResult> replay,
        IReadOnlyDictionary<(Guid PositionId, DateOnly Date), decimal> corrections,
        bool correctionsKnown,
        DateOnly from,
        DateOnly to)
    {
        var profits = new Dictionary<(Guid PositionId, DateOnly Date), decimal?>();

        foreach (var position in positions)
        {
            var series = observations[position.Id];

            foreach (var observation in series.Where(item => item.Date >= from && item.Date <= to))
            {
                var previous = series.LastOrDefault(item => item.Date < observation.Date);
                profits[(position.Id, observation.Date)] = previous is null
                    ? null
                    : CalculatePositionProfit(
                        position.Id,
                        previous.Date,
                        observation.Date,
                        active,
                        replay,
                        corrections,
                        correctionsKnown);
            }
        }

        return profits;
    }

    private static decimal? CalculatePositionProfit(
        Guid positionId,
        DateOnly previousDate,
        DateOnly currentDate,
        IReadOnlyList<BusinessFact> active,
        Func<DateOnly, ReplayResult> replay,
        IReadOnlyDictionary<(Guid PositionId, DateOnly Date), decimal> corrections,
        bool correctionsKnown)
    {
        if (!correctionsKnown)
        {
            return null;
        }

        var current = replay(currentDate).Funds.FirstOrDefault(fund => fund.PositionId == positionId);
        var previous = replay(previousDate).Funds.FirstOrDefault(fund => fund.PositionId == positionId);

        if (current?.Value is null || previous?.Value is null || !current.CanRebuild || !previous.CanRebuild)
        {
            return null;
        }

        var currentValue = current.Value.Value + current.PendingAmount + current.PendingArrivalAmount;
        var previousValue = previous.Value.Value + previous.PendingAmount + previous.PendingArrivalAmount;
        var transfer = active
            .Where(fact => fact.AssetType == AssetType.Fund
                && fact.PositionId == positionId
                && fact.Date > previousDate
                && fact.Date <= currentDate)
            .Sum(fact => -fact.CashEffect);
        var correction = corrections
            .Where(item => item.Key.PositionId == positionId
                && item.Key.Date > previousDate
                && item.Key.Date <= currentDate)
            .Sum(item => item.Value);

        return Round(currentValue - previousValue - transfer - correction);
    }

    private static IReadOnlyList<FundReturnSummaryDto> BuildFundSummaries(
        IReadOnlyList<FundPosition> positions,
        IReadOnlyDictionary<string, List<FundPosition>> fundNames,
        IReadOnlyDictionary<Guid, IReadOnlyList<Observation>> observations,
        IReadOnlyDictionary<(Guid PositionId, DateOnly Date), decimal?> profits,
        IReadOnlyList<BusinessFact> active,
        Func<DateOnly, ReplayResult> replay,
        IReadOnlyDictionary<(Guid PositionId, DateOnly Date), decimal> corrections,
        bool correctionsKnown,
        DateOnly baseline,
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<Guid, string> groupNames)
    {
        var todayReplay = replay(to);
        var openingReplay = baseline <= to ? replay(baseline) : null;

        return fundNames.Select(item =>
        {
            var members = item.Value;
            var memberIds = members.Select(position => position.Id).ToHashSet();
            var notes = new List<string>();
            var closingValue = ScopeValue(todayReplay, members);
            var openingValue = openingReplay is null ? null : ScopeValue(openingReplay, members);
            var holding = CalculateHolding(todayReplay, members);
            var netTransfer = Round(active
                .Where(fact => fact.AssetType == AssetType.Fund
                    && fact.PositionId is not null
                    && memberIds.Contains(fact.PositionId.Value)
                    && fact.Date > baseline
                    && fact.Date <= to)
                .Sum(fact => -fact.CashEffect));
            var correction = correctionsKnown
                ? Round(corrections
                    .Where(item => memberIds.Contains(item.Key.PositionId)
                        && item.Key.Date > baseline
                        && item.Key.Date <= to)
                    .Sum(item => item.Value))
                : (decimal?)null;
            var covered = members.All(position =>
                todayReplay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id)?.CoverageFrom is DateOnly coverage
                && coverage <= baseline);

            if (!covered)
            {
                notes.Add("基金在区间起始后才有持仓依据，区间盈亏不计算。");
            }

            if (correction is null)
            {
                notes.Add("持仓核对缺少行情依据，区间盈亏不计算。");
            }

            decimal? intervalProfit = null;

            if (covered && openingValue is not null && closingValue is not null && correction is not null)
            {
                intervalProfit = Round(closingValue.Value - openingValue.Value - netTransfer - correction.Value);
            }

            var cumulative = Round(profits
                .Where(entry => memberIds.Contains(entry.Key.PositionId)
                    && entry.Key.Date >= from
                    && entry.Key.Date <= to
                    && entry.Value is not null)
                .Sum(entry => entry.Value!.Value));
            var disclosureDays = members
                .SelectMany(position => observations[position.Id]
                    .Where(observation => observation.Date >= from && observation.Date <= to)
                    .Select(observation => observation.Date))
                .Distinct()
                .Count();
            var latestNetValueDate = members
                .Select(position => todayReplay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id)?.PriceDate)
                .Where(date => date is not null)
                .OrderByDescending(date => date)
                .FirstOrDefault();
            var groupId = members.Select(position => position.FundGroupId).FirstOrDefault(id => id is not null);

            return new FundReturnSummaryDto(
                item.Key,
                item.Key,
                groupId,
                groupId is null ? null : groupNames.GetValueOrDefault(groupId.Value),
                openingValue,
                closingValue,
                intervalProfit,
                holding.Profit,
                holding.Rate,
                cumulative,
                disclosureDays,
                latestNetValueDate,
                intervalProfit is not null,
                notes);
        })
        .OrderBy(fund => fund.Name, StringComparer.Ordinal)
        .ToList();
    }

    private static decimal? ScopeValue(ReplayResult replay, IReadOnlyList<FundPosition> positions)
    {
        var values = positions
            .Select(position => replay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id))
            .Select(TotalValue)
            .ToList();

        return values.Any(value => value is null)
            ? null
            : Round(values.Sum(value => value!.Value));
    }

    private static (decimal? Profit, decimal? Rate) CalculateHolding(
        ReplayResult replay,
        IReadOnlyList<FundPosition> positions)
    {
        var funds = positions
            .Select(position => replay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id))
            .ToList();

        if (funds.Any(fund => fund?.Value is null))
        {
            return (null, null);
        }

        var value = funds.Sum(fund => fund!.Value!.Value);
        var cost = funds.Sum(fund => fund!.Cost);
        var profit = Round(value - cost);

        return (profit, cost > 0m ? Round(profit / cost * 100m) : null);
    }

    private static decimal? TotalValue(HoldingReplay? fund)
    {
        return fund?.Value is null
            ? null
            : Round(fund.Value.Value + fund.PendingAmount + fund.PendingArrivalAmount);
    }

    private static FundReturnDto EmptyResult(
        TrendRange range,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<string> notes)
    {
        return new FundReturnDto(
            range,
            from,
            to,
            from,
            null,
            null,
            0m,
            0m,
            null,
            null,
            null,
            null,
            0,
            false,
            [],
            [],
            notes);
    }

    private static decimal Round(decimal value)
    {
        return decimal.Round(value, 2);
    }

    private sealed record Observation(DateOnly Date, decimal Price);
}
