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

        return new GoldOverviewDto(
            holding is not null,
            holding?.Grams ?? 0m,
            holding?.Cost ?? 0m,
            holding?.Quote ?? 0m,
            holding?.QuoteDate,
            holding?.Value ?? 0m,
            averageCost,
            holding?.HoldingProfit ?? 0m,
            holding?.RealizedProfit ?? 0m,
            ledger.CashAccount?.Balance ?? 0m,
            transactions.Select(Map).ToList(),
            holding?.Version ?? 0,
            replay.Gold?.CanRebuild ?? true,
            replay.Gold?.Value is not null);
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


        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateGoldSell(
                ledger.Id,
                request.Grams,
                request.NetAmount,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, null, request.RelatedCashAdjustmentId, cancellationToken);


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
