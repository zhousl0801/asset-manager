using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Funds;

public sealed class FundService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public FundService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<FundOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var transactions = await _ledgerRepository.GetAssetTransactionsAsync(
            ledger.Id,
            AssetType.Fund,
            null,
            null,
            200,
            cancellationToken);
        var positionNames = positions.ToDictionary(position => position.Id, position => position.DisplayName);
        var activities = transactions
            .Select(transaction => Map(transaction, positionNames))
            .ToList();

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var replay = new LedgerReplayCalculator().Calculate(replayData, BusinessFact.From(replayData), _businessDateProvider.Today, currentDate: _businessDateProvider.Today);
        var mappedPositions = positions.Select(position =>
        {
            var state = replay.Funds.Single(item => item.PositionId == position.Id);
            return Map(position) with { HistoryConfirmed = state.CanRebuild, HasEffectiveQuote = state.Value is not null };
        }).ToList();

        return new FundOverviewDto(
            positions.Sum(position => position.Value),
            positions.Sum(position => position.Cost),
            positions.Sum(position => position.HoldingProfit),
            positions.Sum(position => position.RealizedProfit),
            ledger.CashAccount?.Balance ?? 0m,
            mappedPositions,
            activities,
            replay.Funds.All(item => item.CanRebuild),
            replay.Funds.All(item => item.Value is not null));
    }

    public Task<AssetOperationResult<FundMutationResultDto>> CreatePositionAsync(CreateFundPositionRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.opening", request.RequestId, request,
            () => CreatePositionCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> CreatePositionCoreAsync(
        CreateFundPositionRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateValueDate(
            request.NetValueDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        if (request.HoldingDate is null)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请提供起始持仓日期，行情日期不能替代持仓日期。" });
        }

        validationErrors = AssetValidation.ValidateBusinessDate(request.HoldingDate.Value, ledger.StartDate, _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var position = FundPosition.Create(
                ledger.Id,
                request.Name,
                request.Code,
                request.ShareClass,
                request.Platform,
                request.Shares,
                request.Cost,
                request.NetValue,
                request.NetValueDate,
                now);
            var transaction = AssetTransaction.CreateFundOpening(
                ledger.Id,
                position.Id,
                request.Shares,
                request.Cost,
                request.NetValue,
                position.DisplayName,
                request.NetValueDate,
                request.Note,
                request.RequestId,
                now);

            await _ledgerRepository.AddFundPositionAsync(position, cancellationToken);
            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.HoldingDate!.Value, request.NetValueDate, null, cancellationToken);


            return AssetOperationResult<FundMutationResultDto>.Created(
                new FundMutationResultDto(
                    Map(position),
                    ledger.CashAccount.Balance,
                    Map(transaction, position.DisplayName)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<FundMutationResultDto>> BuyAsync(BuyFundRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.buy", request.RequestId, request,
            () => BuyCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> BuyCoreAsync(
        BuyFundRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);
        validationErrors.AddRange(AssetValidation.ValidateValueDate(
            request.NetValueDate,
            _businessDateProvider.Today));

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }


        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            request.PositionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }


        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundBuy(
                ledger.Id,
                position.Id,
                request.Shares,
                request.Amount,
                request.NetValue,
                position.DisplayName,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, request.NetValueDate, request.RelatedCashAdjustmentId, cancellationToken);


            return AssetOperationResult<FundMutationResultDto>.Created(
                new FundMutationResultDto(
                    Map(position),
                    ledger.CashAccount.Balance,
                    Map(transaction, position.DisplayName)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<FundMutationResultDto>> SellAsync(SellFundRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.sell", request.RequestId, request,
            () => SellCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> SellCoreAsync(
        SellFundRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            request.PositionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }


        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundSell(
                ledger.Id,
                position.Id,
                request.Shares,
                request.NetAmount,
                position.DisplayName,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, null, request.RelatedCashAdjustmentId, cancellationToken);


            return AssetOperationResult<FundMutationResultDto>.Created(
                new FundMutationResultDto(
                    Map(position),
                    ledger.CashAccount.Balance,
                    Map(transaction, position.DisplayName)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<FundMutationResultDto>> UpdateQuoteAsync(UpdateFundQuoteRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.quote", request.RequestId, request,
            () => UpdateQuoteCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> UpdateQuoteCoreAsync(
        UpdateFundQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateValueDate(
            request.NetValueDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            request.PositionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundQuote(
                ledger.Id,
                position.Id,
                request.NetValue,
                position.DisplayName,
                request.NetValueDate,
                request.RequestId,
                now);


            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.NetValueDate, request.NetValueDate, null, cancellationToken);


            return AssetOperationResult<FundMutationResultDto>.Success(
                new FundMutationResultDto(
                    Map(position),
                    ledger.CashAccount.Balance,
                    Map(transaction, position.DisplayName)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    private static FundPositionDto Map(FundPosition position)
    {
        return new FundPositionDto(
            position.Id,
            position.Name,
            position.Code,
            position.ShareClass,
            position.Platform,
            position.Shares,
            position.Cost,
            position.NetValue,
            position.NetValueDate,
            position.Value,
            position.HoldingProfit,
            position.RealizedProfit);
    }

    private static FundActivityDto Map(
        AssetTransaction transaction,
        IReadOnlyDictionary<Guid, string> positionNames)
    {
        return Map(transaction, GetPositionName(transaction, positionNames));
    }

    private static FundActivityDto Map(AssetTransaction transaction, string positionName)
    {
        var type = transaction.Kind switch
        {
            AssetTransactionKind.FundOpening or AssetTransactionKind.FundCorrection => FundActivityType.Opening,
            AssetTransactionKind.FundBuy => FundActivityType.Buy,
            AssetTransactionKind.FundSell => FundActivityType.Sell,
            AssetTransactionKind.FundQuote => FundActivityType.Quote,
            _ => throw new InvalidOperationException("不是有效的基金记录。")
        };

        return new FundActivityDto(
            transaction.Id,
            transaction.FundPositionId ?? Guid.Empty,
            positionName,
            type,
            transaction.Quantity,
            transaction.Amount,
            transaction.CashEffect,
            transaction.Price,
            transaction.BusinessDate,
            transaction.Note,
            transaction.CreatedAt);
    }

    private static string GetPositionName(
        AssetTransaction transaction,
        IReadOnlyDictionary<Guid, string> positionNames)
    {
        if (transaction.FundPositionId is not null
            && positionNames.TryGetValue(transaction.FundPositionId.Value, out var positionName))
        {
            return positionName;
        }

        return transaction.Subject ?? "基金";
    }
}
