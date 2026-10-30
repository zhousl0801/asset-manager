using AssetKeeper.Application.History;

namespace AssetKeeper.Application.Ledgers;

public sealed class TrendService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _dates;

    public TrendService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _dates = dates;
    }

    public async Task<AssetTrendDto?> GetAsync(TrendRange range, CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var facts = BusinessFact.From(data);
        var calculator = new LedgerReplayCalculator();
        var today = _dates.Today;
        var from = range switch
        {
            TrendRange.Week => today.AddDays(-6),
            TrendRange.Month => today.AddDays(-29),
            TrendRange.Quarter => today.AddDays(-89),
            TrendRange.Year => new DateOnly(today.Year, 1, 1),
            _ => ledger.StartDate
        };

        if (from < ledger.StartDate)
        {
            from = ledger.StartDate;
        }

        var points = new List<AssetTrendPointDto>();
        var anyIncomplete = false;

        for (var date = from; date <= today; date = date.AddDays(1))
        {
            var result = calculator.Calculate(data, facts, date, currentDate: today);
            var recordedFunds = result.Funds
                .Where(item => item.CoverageFrom is not null && item.CoverageFrom <= date)
                .ToList();
            var fundValue = recordedFunds.Count == 0
                ? (decimal?)null
                : recordedFunds.Any(item => item.Value is null)
                    ? null
                    : recordedFunds.Sum(item => item.Value ?? 0m);
            var hasGold = result.Gold?.CoverageFrom is DateOnly goldFrom && goldFrom <= date;
            var goldValue = hasGold ? result.Gold!.Value : null;
            var recordedStocks = result.Stocks
                .Where(item => item.CoverageFrom is not null && item.CoverageFrom <= date)
                .ToList();
            var stockValue = recordedStocks.Count == 0
                ? (decimal?)null
                : recordedStocks.Any(item => item.Value is null)
                    ? null
                    : recordedStocks.Sum(item => item.Value ?? 0m);
            var point = new AssetTrendPointDto(
                date,
                result.TotalAssets,
                result.KnownTotalAssets,
                result.CashBalance,
                result.ProvidentBalance,
                fundValue,
                recordedFunds.Sum(item => item.PendingAmount),
                recordedFunds.Sum(item => item.PendingArrivalAmount),
                goldValue,
                hasGold ? result.Gold!.PendingArrivalAmount : 0m,
                result.TotalAssets is not null,
                result.CashBalance is not null,
                result.ProvidentBalance is not null,
                recordedFunds.Count > 0,
                hasGold,
                stockValue,
                recordedStocks.Count > 0);
            points.Add(point);
            anyIncomplete |= !point.IsComplete;
        }

        var notes = new List<string>();

        if (anyIncomplete)
        {
            notes.Add("部分日期资产或行情依据不足，完整总资产留空；部分资产合计不作为完整总额。");
        }

        return new AssetTrendDto(range, from, today, points, notes);
    }
}
