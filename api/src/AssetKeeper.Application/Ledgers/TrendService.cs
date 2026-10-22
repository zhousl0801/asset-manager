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
            var fundValue = result.Funds.Count == 0
                ? (decimal?)null
                : result.Funds.Any(item => item.Value is null)
                    ? null
                    : result.Funds.Sum(item => item.Value ?? 0m);
            var goldValue = result.Gold is null ? null : result.Gold.Value;
            var point = new AssetTrendPointDto(
                date,
                result.TotalAssets,
                result.KnownTotalAssets,
                result.CashBalance,
                result.ProvidentBalance,
                fundValue,
                result.Funds.Sum(item => item.PendingAmount),
                result.Funds.Sum(item => item.PendingArrivalAmount),
                goldValue,
                result.Gold?.PendingArrivalAmount ?? 0m,
                result.TotalAssets is not null);
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
