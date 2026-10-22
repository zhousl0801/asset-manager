using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.History;

public sealed class LedgerHistoryRebuilder
{
    private readonly ILedgerRepository _repository;
    private readonly BusinessDateProvider _dates;

    public LedgerHistoryRebuilder(
        ILedgerRepository repository,
        BusinessDateProvider dates)
    {
        _repository = repository;
        _dates = dates;
    }

    public async Task StageAsync(Ledger ledger, AssetTransaction transaction, DateOnly date, DateOnly? priceDate,
        Guid? adjustmentId, CancellationToken cancellationToken)
    {
        var data = await _repository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var isQuote = transaction.Kind is AssetTransactionKind.FundQuote or AssetTransactionKind.GoldQuote;

        if (!isQuote && data.Transactions.Any(item => !item.IsReversed && item.NeedsDateConfirmation
            && item.AssetType == transaction.AssetType && item.FundPositionId == transaction.FundPositionId))
        {
            throw new HistoryValidationException("该资产的起始持仓日期待确认，请先在历史核对中确认原始记录日期。");
        }

        transaction.SetHistoryMetadata(date, priceDate, ledger.AllocateFactSequence());
        transaction.LinkCashAdjustment(adjustmentId);
        await _repository.AddAssetTransactionAsync(transaction, cancellationToken);
        await AddPriceAsync(ledger, transaction, cancellationToken);
        await RebuildAsync(ledger.Id, transaction.Id, cancellationToken);
    }

    public async Task AddPriceAsync(Ledger ledger, AssetTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.Price is decimal price && transaction.PriceDate is DateOnly priceDate && !transaction.IsReversed)
        {
            await _repository.AddPriceObservationAsync(PriceObservation.Create(ledger.Id, transaction.AssetType,
                transaction.FundPositionId, transaction.Id, price, priceDate, _dates.UtcNow, ledger.AllocateFactSequence()), cancellationToken);
        }
    }

    public async Task<ReplayResult> RebuildAsync(Guid ledgerId, Guid changedId, CancellationToken cancellationToken, bool confirmedImpact = false)
    {
        var data = await _repository.GetReplayDataAsync(ledgerId, cancellationToken);
        var facts = BusinessFact.From(data);
        var changed = facts.SingleOrDefault(fact => fact.Id == changedId);

        if (!data.Ledger.CashAccount.HistoryConfirmed && (changed?.CashEffect != 0m || changed?.Source == "adjustment"))
        {
            throw new HistoryValidationException("现金起始依据待确认，请先在历史核对中确认起始现金。");
        }

        var result = new LedgerReplayCalculator().Calculate(data, facts, _dates.Today, currentDate: _dates.Today);

        foreach (var replay in result.Adjustments)
        {
            var adjustment = data.Adjustments.Single(item => item.Id == replay.Id);

            if (!confirmedImpact && adjustment.Id != changedId && adjustment.Difference != replay.Difference
                && changed?.RelatedCashAdjustmentId != adjustment.Id)
            {
                throw new HistoryValidationException($"这笔变动会改变现金核对差额（{adjustment.BusinessDate:yyyy-MM-dd}），请关联漏记业务，或到历史核对预览并确认影响。");
            }
        }

        if (result.CashBalance is decimal cash && (changed?.CashEffect != 0m || changed?.Source == "adjustment"
            || changedId == data.Ledger.CashAccount.Id || cash != data.Ledger.CashAccount.Balance))
        {
            data.Ledger.CashAccount.RestoreProjection(cash, result.CashBalanceDate, _dates.UtcNow);
        }

        if (data.Provident is not null)
        {
            data.Provident.RestoreProjection(result.ProvidentBalance ?? 0m, result.ProvidentBalanceDate, _dates.UtcNow);
        }

        foreach (var fund in result.Funds)
        {
            var position = data.Positions.Single(item => item.Id == fund.PositionId);
            position.RestoreProjection(fund.Quantity, fund.Cost, fund.RealizedProfit, fund.Price ?? position.NetValue,
                fund.PriceDate ?? position.NetValueDate, _dates.UtcNow);
        }

        if (data.Gold is not null && result.Gold is not null)
        {
            data.Gold.RestoreProjection(result.Gold.Quantity, result.Gold.Cost, result.Gold.RealizedProfit,
                result.Gold.Price ?? data.Gold.Quote, result.Gold.PriceDate ?? data.Gold.QuoteDate, _dates.UtcNow);
        }

        foreach (var replay in result.Adjustments)
        {
            data.Adjustments.Single(item => item.Id == replay.Id).Recalculate(replay.PreviousBalance);
        }

        return result;
    }
}
