using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Gold;

public sealed class GoldService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public GoldService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<GoldOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var holding = await _ledgerRepository.GetGoldHoldingAsync(ledger.Id, cancellationToken);
        var transactions = await _ledgerRepository.GetAssetTransactionsAsync(
            ledger.Id,
            AssetType.Gold,
            null,
            null,
            200,
            cancellationToken);
        var averageCost = holding is not null && holding.Grams > 0
            ? decimal.Round(holding.Cost / holding.Grams, 2)
            : (decimal?)null;

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var replay = new LedgerReplayCalculator().Calculate(replayData, BusinessFact.From(replayData), _businessDateProvider.Today, currentDate: _businessDateProvider.Today);
        var arrivedPendingIds = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.GoldSellArrival
                && transaction.LinkedTransactionId is not null)
            .Select(transaction => transaction.LinkedTransactionId!.Value)
            .ToHashSet();
        var pendingArrivals = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.GoldSellPending
                && !arrivedPendingIds.Contains(transaction.Id) && transaction.BusinessDate <= _businessDateProvider.Today)
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .Select(transaction => new PendingGoldArrivalDto(
                transaction.Id,
                transaction.Quantity ?? 0m,
                transaction.Amount,
                transaction.BusinessDate,
                transaction.Note))
            .ToList();
        var pendingArrivalAmount = pendingArrivals.Sum(item => item.Amount);

        return new GoldOverviewDto(
            holding is not null,
            holding?.Grams ?? 0m,
            holding?.Cost ?? 0m,
            holding?.Quote ?? 0m,
            holding?.QuoteDate,
            (holding?.Value ?? 0m) + pendingArrivalAmount,
            averageCost,
            holding?.HoldingProfit ?? 0m,
            holding?.RealizedProfit ?? 0m,
            ledger.CashAccount?.Balance ?? 0m,
            transactions.Select(Map).ToList(),
            holding?.Version ?? 0,
            replay.Gold?.CanRebuild ?? true,
            replay.Gold?.Value is not null,
            pendingArrivalAmount,
            pendingArrivals);
    }

    public Task<AssetOperationResult<GoldMutationResultDto>> BuyAsync(BuyGoldRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "gold.buy", request.RequestId, request,
            () => BuyCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<GoldMutationResultDto>.Conflict,
            message => AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<GoldMutationResultDto>> BuyCoreAsync(
        BuyGoldRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);
        validationErrors.AddRange(AssetValidation.ValidateValueDate(
            request.QuoteDate,
            _businessDateProvider.Today));

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }



        try
        {
            var now = _businessDateProvider.UtcNow;
            var holding = await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken);
            var isNewHolding = holding is null;
            var transaction = AssetTransaction.CreateGoldBuy(
                ledger.Id,
                request.Grams,
                request.Amount,
                request.Quote,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            holding ??= GoldHolding.Create(
                ledger.Id,
                0m,
                0m,
                request.Quote,
                request.QuoteDate,
                now);


            if (isNewHolding)
            {
                await _ledgerRepository.AddGoldHoldingAsync(holding, cancellationToken);
            }

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, request.QuoteDate, request.RelatedCashAdjustmentId, cancellationToken);


            return AssetOperationResult<GoldMutationResultDto>.Created(
                MapResult(holding, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<GoldMutationResultDto>> SellAsync(SellGoldRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "gold.sell", request.RequestId, request,
            () => SellCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<GoldMutationResultDto>.Conflict,
            message => AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<GoldMutationResultDto>> SellCoreAsync(
        SellGoldRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }

        var holding = await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken);

        if (holding is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(
                new[] { "尚未记录积存金持仓。" });
        }

        if (request.Settlement == GoldSellSettlement.PendingArrival && request.RelatedCashAdjustmentId is not null)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(
                new[] { "待到账卖出不产生现金变动，不能关联现金核对差额。" });
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = request.Settlement == GoldSellSettlement.PendingArrival
                ? AssetTransaction.CreateGoldSellPending(
                    ledger.Id,
                    request.Grams,
                    request.NetAmount,
                    request.BusinessDate,
                    request.Note,
                    request.RequestId,
                    now)
                : AssetTransaction.CreateGoldSell(
                    ledger.Id,
                    request.Grams,
                    request.NetAmount,
                    request.BusinessDate,
                    request.Note,
                    request.RequestId,
                    now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                request.BusinessDate,
                null,
                request.Settlement == GoldSellSettlement.Received ? request.RelatedCashAdjustmentId : null,
                cancellationToken);


            return AssetOperationResult<GoldMutationResultDto>.Created(
                MapResult(holding, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<GoldMutationResultDto>> ArriveSellAsync(
        Guid pendingTransactionId,
        GoldSellArrivalRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "gold.sell.arrival",
            request.RequestId,
            new { TransactionId = pendingTransactionId, Request = request },
            () => ArriveSellCoreAsync(pendingTransactionId, request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<GoldMutationResultDto>.Conflict,
            message => AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<GoldMutationResultDto>> ArriveSellCoreAsync(
        Guid pendingTransactionId,
        GoldSellArrivalRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }

        var pending = await _ledgerRepository.GetAssetTransactionForUpdateAsync(
            ledger.Id,
            pendingTransactionId,
            cancellationToken);

        if (pending is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("积存金卖出待到账记录不存在。");
        }

        if (pending.Kind != AssetTransactionKind.GoldSellPending || pending.IsReversed)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "该记录不是有效的积存金卖出待到账。" });
        }

        if (request.BusinessDate < pending.BusinessDate)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "到账日期不能早于卖出日期。" });
        }

        if (request.Amount != pending.Amount)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "到账金额必须与待到账金额一致；如有差额，请先更正卖出记录。" });
        }

        var holding = await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken);

        if (holding is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "尚未记录积存金持仓。" });
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);

        if (replayData.Transactions.Any(transaction => !transaction.IsReversed
            && transaction.Kind == AssetTransactionKind.GoldSellArrival && transaction.LinkedTransactionId == pending.Id))
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "该笔积存金卖出已到账，不能重复收款。" });
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateGoldSellArrival(
                ledger.Id,
                pending.Id,
                request.Amount,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                request.BusinessDate,
                null,
                null,
                cancellationToken);

            return AssetOperationResult<GoldMutationResultDto>.Created(
                MapResult(holding, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<GoldMutationResultDto>> UpdateQuoteAsync(UpdateGoldQuoteRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "gold.quote", request.RequestId, request,
            () => UpdateQuoteCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<GoldMutationResultDto>.Conflict,
            message => AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<GoldMutationResultDto>> UpdateQuoteCoreAsync(
        UpdateGoldQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateValueDate(
            request.QuoteDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var holding = await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken);
            var transaction = AssetTransaction.CreateGoldQuote(
                ledger.Id,
                request.Quote,
                request.QuoteDate,
                request.RequestId,
                now);

            if (holding is null)
            {
                holding = GoldHolding.Create(
                    ledger.Id,
                    0m,
                    0m,
                    request.Quote,
                    request.QuoteDate,
                    now);
                await _ledgerRepository.AddGoldHoldingAsync(holding, cancellationToken);
            }


            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.QuoteDate, request.QuoteDate, null, cancellationToken);


            return AssetOperationResult<GoldMutationResultDto>.Success(
                MapResult(holding, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<GoldMutationResultDto>> SaveHoldingAsync(SaveGoldHoldingRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "gold.holding", request.RequestId, request,
            () => SaveHoldingCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<GoldMutationResultDto>.Conflict,
            message => AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<GoldMutationResultDto>> SaveHoldingCoreAsync(
        SaveGoldHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateValueDate(
            request.QuoteDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }


        if (request.HoldingDate is null)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { "请提供持仓核对日期，行情日期不能替代持仓日期。" });
        }

        validationErrors = AssetValidation.ValidateBusinessDate(request.HoldingDate.Value, ledger.StartDate, _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var holding = await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken);
            if (request.ExpectedVersion != (holding?.Version ?? 0))
            {
                return AssetOperationResult<GoldMutationResultDto>.Conflict("持仓已变化，请刷新后重新核对。");
            }

            var hasHoldingFacts = (await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken)).Transactions
                .Any(item => item.AssetType == AssetType.Gold && item.Kind != AssetTransactionKind.GoldQuote);
            var transaction = AssetTransaction.CreateSnapshot(ledger.Id, null, AssetType.Gold,
                hasHoldingFacts ? AssetTransactionKind.GoldCorrection : AssetTransactionKind.GoldOpening,
                request.Grams, request.Cost, request.Quote, request.HoldingDate.Value, request.QuoteDate,
                request.Note, request.RequestId, now);

            if (holding is null)
            {
                holding = GoldHolding.Create(
                    ledger.Id,
                    request.Grams,
                    request.Cost,
                    request.Quote,
                    request.QuoteDate,
                    now);
                await _ledgerRepository.AddGoldHoldingAsync(holding, cancellationToken);
            }


            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.HoldingDate!.Value, request.QuoteDate, null, cancellationToken);


            return AssetOperationResult<GoldMutationResultDto>.Success(
                MapResult(holding, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<GoldMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    private static GoldMutationResultDto MapResult(
        GoldHolding holding,
        decimal cashBalance,
        AssetTransaction transaction)
    {
        return new GoldMutationResultDto(
            holding.Grams,
            holding.Cost,
            holding.Quote,
            holding.QuoteDate,
            holding.Value,
            cashBalance,
            Map(transaction));
    }

    private static GoldActivityDto Map(AssetTransaction transaction)
    {
        var type = transaction.Kind switch
        {
            AssetTransactionKind.GoldBuy => GoldActivityType.Buy,
            AssetTransactionKind.GoldSell => GoldActivityType.Sell,
            AssetTransactionKind.GoldSellPending => GoldActivityType.SellPending,
            AssetTransactionKind.GoldSellArrival => GoldActivityType.SellArrival,
            AssetTransactionKind.GoldQuote => GoldActivityType.Quote,
            AssetTransactionKind.GoldOpening or AssetTransactionKind.GoldCorrection => GoldActivityType.Correction,
            _ => throw new InvalidOperationException("不是有效的积存金记录。")
        };

        return new GoldActivityDto(
            transaction.Id,
            type,
            transaction.Quantity,
            transaction.Amount,
            transaction.CashEffect,
            transaction.Price,
            transaction.BusinessDate,
            transaction.Note,
            transaction.CreatedAt);
    }
}
