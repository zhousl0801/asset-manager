using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Application.Plans;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Funds;

public sealed class FundService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;
    private readonly IFundQuoteProvider? _quoteProvider;

    public FundService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider)
        : this(ledgerRepository, businessDateProvider, null)
    {
    }

    public FundService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider,
        IFundQuoteProvider? quoteProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
        _quoteProvider = quoteProvider;
    }

    public async Task<FundOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var groups = await _ledgerRepository.GetFundGroupsAsync(ledger.Id, cancellationToken);
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
        var confirmedPendingIds = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.FundBuyConfirm
                && transaction.LinkedTransactionId is not null)
            .Select(transaction => transaction.LinkedTransactionId!.Value)
            .ToHashSet();
        var pendingBuys = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.FundBuyPending
                && !confirmedPendingIds.Contains(transaction.Id) && transaction.BusinessDate <= _businessDateProvider.Today)
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .Select(transaction => new PendingFundBuyDto(
                transaction.Id,
                transaction.FundPositionId ?? Guid.Empty,
                positionNames.GetValueOrDefault(transaction.FundPositionId ?? Guid.Empty) ?? transaction.Subject ?? "基金",
                transaction.Amount,
                transaction.BusinessDate,
                transaction.Note,
                transaction.Fee,
                GetFundTradeDate(transaction),
                GetConfirmationDate(GetFundTradeDate(transaction), transaction.ConfirmationDelayDays),
                CanCancel(transaction)))
            .ToList();
        var pendingAmount = pendingBuys.Sum(item => item.Amount);
        var sellConfirms = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.FundSellConfirm
                && transaction.LinkedTransactionId is not null)
            .ToList();
        var confirmedApplyIds = sellConfirms.Select(transaction => transaction.LinkedTransactionId!.Value).ToHashSet();
        var sellApplications = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.FundSellApply
                && !confirmedApplyIds.Contains(transaction.Id) && transaction.BusinessDate <= _businessDateProvider.Today)
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .Select(transaction => new PendingFundSellDto(
                transaction.Id,
                transaction.FundPositionId ?? Guid.Empty,
                positionNames.GetValueOrDefault(transaction.FundPositionId ?? Guid.Empty) ?? transaction.Subject ?? "基金",
                transaction.Quantity ?? 0m,
                transaction.BusinessDate,
                transaction.Note,
                CanCancel(transaction)))
            .ToList();
        var arrivedConfirmIds = replayData.Transactions
            .Where(transaction => !transaction.IsReversed && transaction.Kind == AssetTransactionKind.FundSellArrival
                && transaction.LinkedTransactionId is not null)
            .Select(transaction => transaction.LinkedTransactionId!.Value)
            .ToHashSet();
        var pendingArrivals = sellConfirms
            .Where(transaction => !arrivedConfirmIds.Contains(transaction.Id)
                && transaction.BusinessDate <= _businessDateProvider.Today)
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .Select(transaction => new PendingFundArrivalDto(
                transaction.Id,
                transaction.FundPositionId ?? Guid.Empty,
                positionNames.GetValueOrDefault(transaction.FundPositionId ?? Guid.Empty) ?? transaction.Subject ?? "基金",
                transaction.Quantity ?? 0m,
                transaction.Amount,
                transaction.BusinessDate,
                transaction.Note))
            .ToList();
        var pendingArrivalAmount = pendingArrivals.Sum(item => item.Amount);
        var groupNames = groups.ToDictionary(group => group.Id, group => group.Name);
        var replayStates = replay.Funds
            .Where(item => item.PositionId is not null)
            .ToDictionary(item => item.PositionId!.Value);
        var mappedPositions = positions.Select(position =>
        {
            var state = replayStates[position.Id];
            return Map(position) with
            {
                HistoryConfirmed = state.CanRebuild,
                HasEffectiveQuote = state.Value is not null,
                GroupId = position.FundGroupId,
                GroupName = position.FundGroupId is null
                    ? null
                    : groupNames.GetValueOrDefault(position.FundGroupId.Value)
            };
        }).ToList();
        var funds = BuildFundSummaries(positions, pendingBuys, pendingArrivals, replayStates, groupNames);
        var groupSummaries = BuildGroupSummaries(groups, positions, funds);

        return new FundOverviewDto(
            positions.Sum(position => position.Value) + pendingAmount + pendingArrivalAmount,
            positions.Sum(position => position.Cost),
            positions.Sum(position => position.HoldingProfit),
            positions.Sum(position => position.RealizedProfit),
            ledger.CashAccount?.Balance ?? 0m,
            mappedPositions,
            activities,
            replay.Funds.All(item => item.CanRebuild),
            replay.Funds.All(item => item.Value is not null),
            pendingAmount,
            pendingBuys,
            pendingArrivalAmount,
            sellApplications,
            pendingArrivals,
            groupSummaries,
            funds);
    }

    private static IReadOnlyList<FundSummaryDto> BuildFundSummaries(
        IReadOnlyList<FundPosition> positions,
        IReadOnlyList<PendingFundBuyDto> pendingBuys,
        IReadOnlyList<PendingFundArrivalDto> pendingArrivals,
        IReadOnlyDictionary<Guid, HoldingReplay> replayStates,
        IReadOnlyDictionary<Guid, string> groupNames)
    {
        var pendingBuyAmounts = pendingBuys
            .GroupBy(item => item.PositionId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var pendingArrivalAmounts = pendingArrivals
            .GroupBy(item => item.PositionId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        var familyKeys = FundIdentity.FamilyKeys(positions);
        var effectiveGroupIds = FundIdentity.EffectiveGroupIds(positions);

        return positions
            .GroupBy(position => familyKeys[position.Id], StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.OrderBy(position => position.ShareClass).ThenBy(position => position.Platform).ToList();
                var classes = members
                    .GroupBy(FundIdentity.ClassKey, StringComparer.Ordinal)
                    .Select(classGroup =>
                    {
                        var classMembers = classGroup
                            .OrderBy(position => position.Platform)
                            .ThenBy(position => position.Id)
                            .ToList();
                        var netValues = classMembers.Select(position => position.NetValue).Distinct().ToList();
                        var netValueDates = classMembers.Select(position => position.NetValueDate).Distinct().ToList();

                        return new FundShareClassDto(
                            string.IsNullOrWhiteSpace(classMembers[0].ShareClass) ? null : FundIdentity.Normalize(classMembers[0].ShareClass),
                            decimal.Round(classMembers.Sum(position => position.Shares), 4),
                            decimal.Round(classMembers.Sum(position => position.Cost), 2),
                            decimal.Round(classMembers.Sum(position => position.Value), 2),
                            decimal.Round(classMembers.Sum(position => position.HoldingProfit), 2),
                            decimal.Round(classMembers.Sum(position => position.RealizedProfit), 2),
                            netValues.Count == 1 ? netValues[0] : null,
                            netValueDates.Count == 1 ? netValueDates[0] : null,
                            classMembers.Select(position => new FundPlatformHoldingDto(
                                position.Id,
                                position.Platform,
                                position.Name,
                                position.Code,
                                position.ShareClass,
                                position.Shares,
                                position.Cost,
                                position.NetValue,
                                position.NetValueDate,
                                position.Value,
                                position.HoldingProfit,
                                position.RealizedProfit,
                                replayStates[position.Id].Value is not null,
                                position.ConfirmationDelayDays)).ToList(),
                            classGroup.Key,
                            classMembers[0].Code,
                            string.IsNullOrWhiteSpace(classMembers[0].Code) ? "按规范化基金名称和类别匹配" : "按基金代码和类别匹配");
                    })
                    .OrderBy(fundClass => fundClass.ShareClass, StringComparer.Ordinal)
                    .ToList();
                var pendingBuyAmount = members.Sum(position => pendingBuyAmounts.GetValueOrDefault(position.Id));
                var pendingArrivalAmount = members.Sum(position => pendingArrivalAmounts.GetValueOrDefault(position.Id));
                var value = decimal.Round(members.Sum(position => position.Value), 2);
                var groupId = effectiveGroupIds[members[0].Id];
                var platformCount = members
                    .Select(position => position.Platform)
                    .Where(platform => !string.IsNullOrWhiteSpace(platform))
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                return new FundSummaryDto(
                    group.Key,
                    members[0].Name,
                    groupId,
                    groupId is null ? null : groupNames.GetValueOrDefault(groupId.Value),
                    value,
                    decimal.Round(members.Sum(position => position.Cost), 2),
                    decimal.Round(members.Sum(position => position.HoldingProfit), 2),
                    decimal.Round(members.Sum(position => position.RealizedProfit), 2),
                    decimal.Round(pendingBuyAmount, 2),
                    decimal.Round(pendingArrivalAmount, 2),
                    decimal.Round(value + pendingBuyAmount + pendingArrivalAmount, 2),
                    platformCount,
                    members.Max(position => position.NetValueDate),
                    members.All(position => replayStates[position.Id].CanRebuild),
                    members.All(position => replayStates[position.Id].Value is not null),
                    classes);
            })
            .OrderBy(fund => fund.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<FundGroupDto> BuildGroupSummaries(
        IReadOnlyList<FundGroup> groups,
        IReadOnlyList<FundPosition> positions,
        IReadOnlyList<FundSummaryDto> funds)
    {
        return groups
            .Select(group => new FundGroupDto(
                group.Id,
                group.Name,
                group.SortOrder,
                funds.Count(fund => fund.GroupId == group.Id),
                funds.Where(fund => fund.GroupId == group.Id).Sum(fund => fund.Classes.Sum(fundClass => fundClass.Platforms.Count)),
                decimal.Round(funds.Where(fund => fund.GroupId == group.Id).Sum(fund => fund.Value), 2)))
            .ToList();
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

        if (_quoteProvider is null || string.IsNullOrWhiteSpace(request.Code))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请填写基金代码，并确保行情服务可用；持仓净值只能从数据源获取。" });
        }

        if (request.HoldingDate is null)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "请提供起始持仓日期，行情日期不能替代持仓日期。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(request.HoldingDate.Value, ledger.StartDate, _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var quote = await _quoteProvider.GetLatestAsync(request.Code, cancellationToken);
            validationErrors = AssetValidation.ValidateValueDate(quote.EffectiveDate, _businessDateProvider.Today);
            if (validationErrors.Count > 0 || quote.NetValue <= 0)
            {
                return AssetOperationResult<FundMutationResultDto>.Unavailable("行情源返回的最新净值无效。");
            }

            var position = FundPosition.Create(
                ledger.Id,
                request.Name,
                request.Code,
                request.ShareClass,
                request.Platform,
                request.Shares,
                request.Cost,
                quote.NetValue,
                quote.EffectiveDate,
                now,
                request.ConfirmationDelayDays);
            var transaction = AssetTransaction.CreateFundOpening(
                ledger.Id,
                position.Id,
                request.Shares,
                request.Cost,
                quote.NetValue,
                position.DisplayName,
                quote.EffectiveDate,
                request.Note,
                request.RequestId,
                now,
                quote.Source);

            position.RecordQuoteSyncAttempt(now, quote.Source, null);

            await _ledgerRepository.AddFundPositionAsync(position, cancellationToken);
            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.HoldingDate!.Value, quote.EffectiveDate, null, cancellationToken);


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
        catch (QuoteUnavailableException exception)
        {
            return AssetOperationResult<FundMutationResultDto>.Unavailable(exception.Message);
        }
        catch (HttpRequestException)
        {
            return AssetOperationResult<FundMutationResultDto>.Unavailable("基金行情数据源暂不可用，未创建持仓。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AssetOperationResult<FundMutationResultDto>.Unavailable("基金行情数据源请求超时，未创建持仓。");
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

        if (string.IsNullOrWhiteSpace(position.Code))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该基金未设置基金代码，无法按数据源净值自动确认份额。" });
        }


        try
        {
            var now = _businessDateProvider.UtcNow;
            var tradeDate = ResolveBuyTradeDate(request.BusinessDate, now);
            var transaction = AssetTransaction.CreateFundBuyPending(
                ledger.Id,
                position.Id,
                request.Amount,
                position.DisplayName,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now,
                request.Fee,
                position.ConfirmationDelayDays,
                tradeDate);

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

    public Task<AssetOperationResult<FundMutationResultDto>> ConfirmBuyAsync(
        Guid transactionId,
        ConfirmFundBuyRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.buy.confirm",
            request.RequestId,
            new { TransactionId = transactionId, Request = request },
            () => ConfirmBuyCoreAsync(transactionId, request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> ConfirmBuyCoreAsync(
        Guid transactionId,
        ConfirmFundBuyRequest request,
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

        if (request.NetValueDate > request.BusinessDate)
        {
            validationErrors.Add("净值日期不能晚于确认日期。");
        }

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        var pending = await _ledgerRepository.GetAssetTransactionForUpdateAsync(
            ledger.Id,
            transactionId,
            cancellationToken);

        if (pending is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("买入待确认记录不存在。");
        }

        if (pending.Kind != AssetTransactionKind.FundBuyPending || pending.IsReversed)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该记录不是有效的买入待确认。" });
        }

        if (request.Fee != pending.Fee)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "确认费用必须沿用待确认交易中记录的实际申购费。" });
        }

        if (request.BusinessDate < pending.BusinessDate)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "确认日期不能早于买入日期。" });
        }

        if (pending.FundPositionId is not Guid positionId)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "待确认买入缺少基金持仓。" });
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            positionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);

        if (replayData.Transactions.Any(transaction => !transaction.IsReversed
            && transaction.Kind == AssetTransactionKind.FundBuyConfirm && transaction.LinkedTransactionId == pending.Id))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该买入待确认已确认，不能重复结转。" });
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundBuyConfirm(
                ledger.Id,
                position.Id,
                pending.Id,
                request.Shares,
                pending.Amount,
                request.NetValue,
                request.Fee,
                position.DisplayName,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                request.BusinessDate,
                request.NetValueDate,
                null,
                cancellationToken);

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

    public Task<AssetOperationResult<FundMutationResultDto>> ApplySellAsync(SellFundRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.sell.apply", request.RequestId, request,
            () => ApplySellCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> ApplySellCoreAsync(
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
            var transaction = AssetTransaction.CreateFundSellApply(
                ledger.Id,
                position.Id,
                request.Shares,
                position.DisplayName,
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

    public Task<AssetOperationResult<FundMutationResultDto>> ConfirmSellAsync(
        Guid applyTransactionId,
        ConfirmFundSellRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.sell.confirm",
            request.RequestId,
            new { TransactionId = applyTransactionId, Request = request },
            () => ConfirmSellCoreAsync(applyTransactionId, request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> ConfirmSellCoreAsync(
        Guid applyTransactionId,
        ConfirmFundSellRequest request,
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

        if (request.NetValueDate > request.BusinessDate)
        {
            validationErrors.Add("净值日期不能晚于确认日期。");
        }

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(validationErrors);
        }

        var apply = await _ledgerRepository.GetAssetTransactionForUpdateAsync(
            ledger.Id,
            applyTransactionId,
            cancellationToken);

        if (apply is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("赎回申请记录不存在。");
        }

        if (apply.Kind != AssetTransactionKind.FundSellApply || apply.IsReversed)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该记录不是有效的赎回申请。" });
        }

        if (request.BusinessDate < apply.BusinessDate)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "确认日期不能早于申请日期。" });
        }

        if (request.Shares > (apply.Quantity ?? 0m))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "确认份额不能超过申请赎回份额。" });
        }

        if (apply.FundPositionId is not Guid positionId)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "赎回申请缺少基金持仓。" });
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            positionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);

        if (replayData.Transactions.Any(transaction => !transaction.IsReversed
            && transaction.Kind == AssetTransactionKind.FundSellConfirm && transaction.LinkedTransactionId == apply.Id))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该赎回申请已确认，不能重复结转。" });
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundSellConfirm(
                ledger.Id,
                position.Id,
                apply.Id,
                request.Shares,
                request.NetValue,
                request.NetAmount,
                request.Fee,
                position.DisplayName,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                request.BusinessDate,
                request.NetValueDate,
                null,
                cancellationToken);

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

    public Task<AssetOperationResult<FundMutationResultDto>> ArriveSellAsync(
        Guid confirmTransactionId,
        FundSellArrivalRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.sell.arrival",
            request.RequestId,
            new { TransactionId = confirmTransactionId, Request = request },
            () => ArriveSellCoreAsync(confirmTransactionId, request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> ArriveSellCoreAsync(
        Guid confirmTransactionId,
        FundSellArrivalRequest request,
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

        var confirm = await _ledgerRepository.GetAssetTransactionForUpdateAsync(
            ledger.Id,
            confirmTransactionId,
            cancellationToken);

        if (confirm is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("赎回确认记录不存在。");
        }

        if (confirm.Kind != AssetTransactionKind.FundSellConfirm || confirm.IsReversed)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该记录不是有效的赎回确认。" });
        }

        if (request.BusinessDate < confirm.BusinessDate)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "到账日期不能早于确认日期。" });
        }

        if (request.Amount != confirm.Amount)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。" });
        }

        if (confirm.FundPositionId is not Guid positionId)
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "赎回确认缺少基金持仓。" });
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(
            ledger.Id,
            positionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);

        if (replayData.Transactions.Any(transaction => !transaction.IsReversed
            && transaction.Kind == AssetTransactionKind.FundSellArrival && transaction.LinkedTransactionId == confirm.Id))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该赎回确认已到账，不能重复收款。" });
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateFundSellArrival(
                ledger.Id,
                position.Id,
                confirm.Id,
                request.Amount,
                position.DisplayName,
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

    public async Task<AssetOperationResult<FundQuoteRefreshDto>> RefreshQuoteAsync(
        Guid positionId,
        CancellationToken cancellationToken,
        bool confirmDueBuys = true)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<FundQuoteRefreshDto>.NotFound("账本尚未初始化。");
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, positionId, cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<FundQuoteRefreshDto>.NotFound("基金持仓不存在。");
        }

        if (_quoteProvider is null)
        {
            return AssetOperationResult<FundQuoteRefreshDto>.Unavailable("未配置基金净值行情服务。");
        }

        if (string.IsNullOrWhiteSpace(position.Code))
        {
            return AssetOperationResult<FundQuoteRefreshDto>.Invalid(new[] { "该基金未填写基金代码，无法获取净值。" });
        }

        var now = _businessDateProvider.UtcNow;

        try
        {
            var quote = await _quoteProvider.GetLatestAsync(position.Code, cancellationToken);
            var dateErrors = AssetValidation.ValidateValueDate(quote.EffectiveDate, _businessDateProvider.Today);

            if (dateErrors.Count > 0 || quote.NetValue <= 0)
            {
                throw new QuoteUnavailableException("行情服务返回的净值或有效日期无效。");
            }

            var latest = await _ledgerRepository.GetLatestPriceObservationAsync(
                ledger.Id,
                AssetType.Fund,
                position.Id,
                cancellationToken);
            var updated = latest is null
                || latest.Price != quote.NetValue
                || latest.EffectiveDate != quote.EffectiveDate
                || latest.Source != quote.Source;

            if (updated)
            {
                var transaction = AssetTransaction.CreateFundQuote(
                    ledger.Id,
                    position.Id,
                    quote.NetValue,
                    position.DisplayName,
                    quote.EffectiveDate,
                    Guid.NewGuid(),
                    now,
                    quote.Source);

                await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                    ledger,
                    transaction,
                    quote.EffectiveDate,
                    quote.EffectiveDate,
                    null,
                    cancellationToken);
            }

            position.RecordQuoteSyncAttempt(now, quote.Source, null);
            ledger.Touch(now);
            await _ledgerRepository.SaveChangesAsync(cancellationToken);

            if (confirmDueBuys)
            {
                await ConfirmDueBuysAsync(cancellationToken);
            }

            return AssetOperationResult<FundQuoteRefreshDto>.Success(new FundQuoteRefreshDto(
                Map(position),
                updated,
                quote.Source,
                quote.NetValue,
                quote.EffectiveDate,
                now,
                now));
        }
        catch (Exception exception) when (exception is QuoteUnavailableException or HttpRequestException or TaskCanceledException
            or ArgumentException or InvalidOperationException or HistoryValidationException)
        {
            position.RecordQuoteSyncAttempt(now, null, Shorten(exception.Message));
            ledger.Touch(now);

            try
            {
                await _ledgerRepository.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                _ledgerRepository.DiscardChanges();
                return AssetOperationResult<FundQuoteRefreshDto>.Conflict("数据已被其他操作修改，请刷新后重试。");
            }

            return AssetOperationResult<FundQuoteRefreshDto>.Unavailable(exception.Message);
        }
        catch (ConcurrencyConflictException)
        {
            _ledgerRepository.DiscardChanges();
            return AssetOperationResult<FundQuoteRefreshDto>.Conflict("数据已被其他操作修改，请刷新后重试。");
        }
    }

    public async Task<int> ConfirmDueBuysAsync(CancellationToken cancellationToken)
    {
        if (_quoteProvider is null)
        {
            return 0;
        }

        var ledger = await _ledgerRepository.GetAsync(cancellationToken);
        if (ledger is null)
        {
            return 0;
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var confirmedIds = replayData.Transactions
            .Where(transaction => !transaction.IsReversed
                && transaction.Kind == AssetTransactionKind.FundBuyConfirm
                && transaction.LinkedTransactionId is not null)
            .Select(transaction => transaction.LinkedTransactionId!.Value)
            .ToHashSet();
        var positions = replayData.Positions.ToDictionary(position => position.Id);
        var pending = replayData.Transactions
            .Where(transaction => !transaction.IsReversed
                && transaction.Kind == AssetTransactionKind.FundBuyPending
                && !confirmedIds.Contains(transaction.Id))
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .ToList();
        var confirmedCount = 0;

        foreach (var transaction in pending)
        {
            var tradeDate = GetFundTradeDate(transaction);
            var confirmationDate = GetConfirmationDate(tradeDate, transaction.ConfirmationDelayDays);
            if (_businessDateProvider.Today < confirmationDate
                || transaction.FundPositionId is not Guid positionId
                || !positions.TryGetValue(positionId, out var position)
                || string.IsNullOrWhiteSpace(position.Code))
            {
                continue;
            }

            try
            {
                var quote = await _quoteProvider.GetForDateAsync(position.Code, tradeDate, cancellationToken);
                var shares = decimal.Round((transaction.Amount - transaction.Fee) / quote.NetValue, 4, MidpointRounding.ToZero);
                if (shares <= 0m || quote.EffectiveDate != tradeDate)
                {
                    continue;
                }

                var result = await ConfirmBuyAsync(transaction.Id, new ConfirmFundBuyRequest(
                    shares,
                    quote.NetValue,
                    quote.EffectiveDate,
                    transaction.Fee,
                    confirmationDate,
                    "行情自动确认",
                    Guid.NewGuid()), cancellationToken);
                if (result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success)
                {
                    confirmedCount++;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && (exception is QuoteUnavailableException or HttpRequestException or TaskCanceledException))
            {
                // Historical NAV may be delayed by the data source; the next refresh retries it.
            }
        }

        return confirmedCount;
    }

    public Task<AssetOperationResult<FundMutationResultDto>> CancelPendingAsync(
        Guid transactionId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.pending.cancel",
            requestId,
            new { TransactionId = transactionId },
            () => CancelPendingCoreAsync(transactionId, cancellationToken),
            result => result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> CancelPendingCoreAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);
        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        var transaction = await _ledgerRepository.GetAssetTransactionForUpdateAsync(ledger.Id, transactionId, cancellationToken);
        if (transaction is null || transaction.IsReversed
            || transaction.Kind is not (AssetTransactionKind.FundBuyPending or AssetTransactionKind.FundSellApply))
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("待处理基金交易不存在或已处理。");
        }

        if (!CanCancel(transaction))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "基金交易仅在北京时间 15:00 前可撤销。" });
        }

        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        if (replayData.Transactions.Any(item => !item.IsReversed
            && (item.Kind is AssetTransactionKind.FundBuyConfirm or AssetTransactionKind.FundSellConfirm)
            && item.LinkedTransactionId == transaction.Id))
        {
            return AssetOperationResult<FundMutationResultDto>.Invalid(new[] { "该申请已确认，不能撤销。" });
        }

        transaction.Reverse();
        await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).RebuildAsync(ledger.Id, transaction.Id, cancellationToken);
        var positionName = transaction.Subject ?? "基金";
        var position = transaction.FundPositionId is Guid positionId
            ? await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, positionId, cancellationToken)
            : null;

        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        return AssetOperationResult<FundMutationResultDto>.Success(new FundMutationResultDto(
            Map(position),
            ledger.CashAccount?.Balance ?? 0m,
            Map(transaction, position?.DisplayName ?? positionName)));
    }

    public Task<AssetOperationResult<FundMutationResultDto>> CorrectSharesAsync(
        CorrectFundSharesRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "fund.shares.correct",
            request.RequestId,
            request,
            () => CorrectSharesCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<FundMutationResultDto>> CorrectSharesCoreAsync(
        CorrectFundSharesRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);
        if (ledger is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("账本尚未初始化。");
        }

        var position = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, request.PositionId, cancellationToken);
        if (position is null)
        {
            return AssetOperationResult<FundMutationResultDto>.NotFound("基金持仓不存在。");
        }

        try
        {
            var transaction = AssetTransaction.CreateSnapshot(
                ledger.Id,
                position.Id,
                AssetType.Fund,
                AssetTransactionKind.FundCorrection,
                request.Shares,
                request.Shares == 0m ? 0m : position.Cost,
                null,
                _businessDateProvider.Today,
                null,
                request.Note ?? "持仓份额核对",
                request.RequestId,
                _businessDateProvider.UtcNow);
            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                _businessDateProvider.Today,
                null,
                null,
                cancellationToken);

            return AssetOperationResult<FundMutationResultDto>.Success(new FundMutationResultDto(
                Map(position),
                ledger.CashAccount?.Balance ?? 0m,
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

    private static string Shorten(string message)
    {
        return message.Length <= 300 ? message : message[..300];
    }

    private DateOnly ResolveBuyTradeDate(DateOnly requestedDate, DateTimeOffset now)
    {
        if (requestedDate != _businessDateProvider.Today)
        {
            return requestedDate;
        }

        var today = _businessDateProvider.Today;
        if (!TradingCalendar.Default.GetDay(today).IsTradingDay)
        {
            return TradingCalendar.Default.NextTradingDay(today.AddDays(-1));
        }

        return now >= _businessDateProvider.ToBusinessInstant(today, new TimeOnly(15, 0))
            ? TradingCalendar.Default.NextTradingDay(today)
            : today;
    }

    private bool CanCancel(AssetTransaction transaction)
    {
        var now = _businessDateProvider.UtcNow;
        var today = _businessDateProvider.Today;
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(Ledger.DefaultTimeZone);
        var createdDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timezone).DateTime);
        var transactionDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(transaction.CreatedAt, timezone).DateTime);
        return createdDate == today
            && transactionDate == today
            && now < _businessDateProvider.ToBusinessInstant(today, new TimeOnly(15, 0));
    }

    private static DateOnly GetConfirmationDate(DateOnly tradeDate, int delayDays)
    {
        var date = tradeDate;
        for (var index = 0; index < delayDays; index++)
        {
            date = TradingCalendar.Default.NextTradingDay(date);
        }

        return date;
    }

    private static DateOnly GetFundTradeDate(AssetTransaction transaction) => transaction.FundTradeDate ?? transaction.BusinessDate;

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
            position.RealizedProfit,
            LastQuoteAttemptAt: position.LastQuoteAttemptAt,
            LastQuoteSource: position.LastQuoteSource,
            LastQuoteError: position.LastQuoteError,
            ConfirmationDelayDays: position.ConfirmationDelayDays);
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
            AssetTransactionKind.FundOpening => FundActivityType.Opening,
            AssetTransactionKind.FundCorrection => FundActivityType.Correction,
            AssetTransactionKind.FundBuy => FundActivityType.Buy,
            AssetTransactionKind.FundBuyPending => FundActivityType.BuyPending,
            AssetTransactionKind.FundBuyConfirm => FundActivityType.BuyConfirmed,
            AssetTransactionKind.FundSell => FundActivityType.Sell,
            AssetTransactionKind.FundSellApply => FundActivityType.SellApply,
            AssetTransactionKind.FundSellConfirm => FundActivityType.SellConfirm,
            AssetTransactionKind.FundSellArrival => FundActivityType.SellArrival,
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
            transaction.CreatedAt,
            transaction.Fee,
            transaction.LinkedTransactionId,
            transaction.IsReversed);
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
