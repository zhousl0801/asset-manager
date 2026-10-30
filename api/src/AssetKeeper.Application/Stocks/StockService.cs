using AssetKeeper.Application.Assets;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Stocks;

public sealed class StockService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;
    private readonly IStockQuoteProvider? _quoteProvider;

    public StockService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider)
        : this(ledgerRepository, businessDateProvider, null)
    {
    }

    public StockService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider,
        IStockQuoteProvider? quoteProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
        _quoteProvider = quoteProvider;
    }

    public async Task<StockOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var positions = await _ledgerRepository.GetStockPositionsAsync(ledger.Id, cancellationToken);
        var transactions = await _ledgerRepository.GetAssetTransactionsAsync(
            ledger.Id,
            AssetType.Stock,
            null,
            null,
            200,
            cancellationToken);
        var positionNames = positions.ToDictionary(position => position.Id, position => position.Name);
        var activities = transactions
            .Where(transaction => transaction.Kind != AssetTransactionKind.StockQuote)
            .Select(transaction => MapActivity(transaction, positionNames))
            .ToList();
        var replayData = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var replay = new LedgerReplayCalculator().Calculate(
            replayData,
            BusinessFact.From(replayData),
            _businessDateProvider.Today,
            currentDate: _businessDateProvider.Today);
        var replayStates = replay.Stocks
            .Where(item => item.PositionId is not null)
            .ToDictionary(item => item.PositionId!.Value);
        var todayProfits = CalculateTodayProfits(replayData, positions);
        var mappedPositions = positions.Select(position =>
        {
            replayStates.TryGetValue(position.Id, out var state);
            var hasTodayProfit = todayProfits.TryGetValue(position.Id, out var todayProfit);

            return MapPosition(position, state, hasTodayProfit ? todayProfit : null);
        }).ToList();
        var todayProfitDate = positions.Count == 0
            ? (DateOnly?)null
            : positions.Max(position => position.PriceDate);
        var todayProfitComplete = positions.Count == 0
            || positions.All(position => todayProfits.ContainsKey(position.Id));
        var todayProfit = positions.Count == 0
            ? 0m
            : todayProfits.Count == 0
                ? (decimal?)null
                : decimal.Round(todayProfits.Values.Sum(), 2);

        return new StockOverviewDto(
            positions.Count > 0,
            positions.Count,
            positions.Sum(position => position.Cost),
            positions.Sum(position => position.Value),
            todayProfit,
            todayProfitComplete,
            todayProfitDate,
            positions.Sum(position => position.HoldingProfit),
            positions.Sum(position => position.RealizedProfit),
            ledger.CashAccount?.Balance ?? 0m,
            mappedPositions,
            activities,
            replay.Stocks.All(item => item.CanRebuild),
            replay.Stocks.All(item => item.Value is not null));
    }

    public async Task<IReadOnlyList<StockSearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (_quoteProvider is null || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = await _quoteProvider.SearchAsync(query.Trim(), cancellationToken);

        return results
            .Select(item => new StockSearchResultDto(
                item.Market,
                item.Code,
                item.Name,
                item.Currency,
                item.Price,
                item.PriceDate))
            .ToList();
    }

    public async Task<AssetOperationResult<StockQuoteDto>> GetQuoteAsync(
        StockMarket market,
        string code,
        CancellationToken cancellationToken)
    {
        if (_quoteProvider is null)
        {
            return AssetOperationResult<StockQuoteDto>.Unavailable("未配置股票行情服务。");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return AssetOperationResult<StockQuoteDto>.Invalid(["请提供股票代码。"]);
        }

        try
        {
            var quote = await _quoteProvider.GetLatestAsync(market, NormalizeCode(code), cancellationToken);
            var dateErrors = AssetValidation.ValidateValueDate(quote.PriceDate, _businessDateProvider.Today);

            if (dateErrors.Count > 0 || quote.Price <= 0m)
            {
                return AssetOperationResult<StockQuoteDto>.Unavailable("行情服务返回的价格或行情日期无效。");
            }

            return AssetOperationResult<StockQuoteDto>.Success(new StockQuoteDto(
                quote.Market,
                quote.Code,
                quote.Price,
                quote.PriceDate,
                quote.PreviousClose is > 0m ? quote.PreviousClose : null,
                quote.Source));
        }
        catch (QuoteUnavailableException exception)
        {
            return AssetOperationResult<StockQuoteDto>.Unavailable(exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return AssetOperationResult<StockQuoteDto>.Unavailable("股票行情服务暂时无法连接，请稍后重试。");
        }
    }

    public Task<AssetOperationResult<StockMutationResultDto>> BuyAsync(
        BuyStockRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "stock.buy",
            request.RequestId,
            request,
            () => BuyCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<StockMutationResultDto>.Conflict,
            message => AssetOperationResult<StockMutationResultDto>.Invalid([message]),
            cancellationToken);
    }

    private async Task<AssetOperationResult<StockMutationResultDto>> BuyCoreAsync(
        BuyStockRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<StockMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(["请求标识无效。"]);
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var code = NormalizeCode(request.Code);
            var position = await _ledgerRepository.FindStockPositionForUpdateAsync(
                ledger.Id,
                request.Market,
                code,
                cancellationToken);
            var isNew = position is null;

            if (position is null)
            {
                position = StockPosition.Create(
                    ledger.Id,
                    request.Market,
                    code,
                    string.IsNullOrWhiteSpace(request.Name) ? code : request.Name,
                    "CNY",
                    0m,
                    0m,
                    request.Price,
                    request.BusinessDate,
                    now);
                await _ledgerRepository.AddStockPositionAsync(position, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.Name))
            {
                position.UpdateProfile(request.Name, now);
            }

            var gross = decimal.Round(request.Shares * request.Price, 2);
            var amount = gross + request.Fee;
            var transaction = AssetTransaction.CreateStockBuy(
                ledger.Id,
                position.Id,
                request.Shares,
                amount,
                request.Price,
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
                request.BusinessDate,
                null,
                cancellationToken);

            return AssetOperationResult<StockMutationResultDto>.Created(
                MapResult(position, ledger.CashAccount.Balance, transaction, isNew));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
    }

    public Task<AssetOperationResult<StockMutationResultDto>> SellAsync(
        SellStockRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "stock.sell",
            request.RequestId,
            request,
            () => SellCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<StockMutationResultDto>.Conflict,
            message => AssetOperationResult<StockMutationResultDto>.Invalid([message]),
            cancellationToken);
    }

    private async Task<AssetOperationResult<StockMutationResultDto>> SellCoreAsync(
        SellStockRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<StockMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(["请求标识无效。"]);
        }

        if (request.PositionId == Guid.Empty)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(["股票持仓标识无效。"]);
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(validationErrors);
        }

        var position = await _ledgerRepository.GetStockPositionForUpdateAsync(
            ledger.Id,
            request.PositionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<StockMutationResultDto>.NotFound("股票持仓不存在。");
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            var gross = decimal.Round(request.Shares * request.Price, 2);
            var amount = gross - request.Fee;
            var transaction = AssetTransaction.CreateStockSell(
                ledger.Id,
                position.Id,
                request.Shares,
                amount,
                request.Price,
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
                request.BusinessDate,
                null,
                cancellationToken);

            return AssetOperationResult<StockMutationResultDto>.Created(
                MapResult(position, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
    }

    public Task<AssetOperationResult<StockMutationResultDto>> SaveHoldingAsync(
        SaveStockHoldingRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "stock.holding",
            request.RequestId,
            request,
            () => SaveHoldingCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<StockMutationResultDto>.Conflict,
            message => AssetOperationResult<StockMutationResultDto>.Invalid([message]),
            cancellationToken);
    }

    private async Task<AssetOperationResult<StockMutationResultDto>> SaveHoldingCoreAsync(
        SaveStockHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<StockMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(["请求标识无效。"]);
        }

        var validationErrors = AssetValidation.ValidateValueDate(
            request.PriceDate,
            _businessDateProvider.Today);
        validationErrors.AddRange(AssetValidation.ValidateBusinessDate(
            request.HoldingDate,
            ledger.StartDate,
            _businessDateProvider.Today));

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid(validationErrors);
        }

        try
        {
            var now = _businessDateProvider.UtcNow;
            StockPosition? position;

            if (request.PositionId is Guid positionId && positionId != Guid.Empty)
            {
                position = await _ledgerRepository.GetStockPositionForUpdateAsync(
                    ledger.Id,
                    positionId,
                    cancellationToken);

                if (position is null)
                {
                    return AssetOperationResult<StockMutationResultDto>.NotFound("股票持仓不存在。");
                }
            }
            else
            {
                position = await _ledgerRepository.FindStockPositionForUpdateAsync(
                    ledger.Id,
                    request.Market,
                    NormalizeCode(request.Code),
                    cancellationToken);
            }

            if (request.ExpectedVersion != (position?.Version ?? 0))
            {
                return AssetOperationResult<StockMutationResultDto>.Conflict("持仓已变化，请刷新后重新核对。");
            }

            if (position is null)
            {
                position = StockPosition.Create(
                    ledger.Id,
                    request.Market,
                    NormalizeCode(request.Code),
                    request.Name,
                    "CNY",
                    request.Shares,
                    request.Cost,
                    request.Price,
                    request.PriceDate,
                    now);
                await _ledgerRepository.AddStockPositionAsync(position, cancellationToken);
            }
            else
            {
                position.UpdateProfile(request.Name, now);
            }

            var hasHoldingFacts = (await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken)).Transactions
                .Any(item => !item.IsReversed
                    && item.AssetType == AssetType.Stock
                    && item.StockPositionId == position.Id
                    && item.Kind != AssetTransactionKind.StockQuote);
            var kind = hasHoldingFacts
                ? AssetTransactionKind.StockCorrection
                : AssetTransactionKind.StockOpening;
            var transaction = AssetTransaction.CreateSnapshot(
                ledger.Id,
                position.Id,
                AssetType.Stock,
                kind,
                request.Shares,
                request.Cost,
                request.Price,
                request.HoldingDate,
                request.PriceDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                ledger,
                transaction,
                request.HoldingDate,
                request.PriceDate,
                null,
                cancellationToken);

            return AssetOperationResult<StockMutationResultDto>.Success(
                MapResult(position, ledger.CashAccount.Balance, transaction));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<StockMutationResultDto>.Invalid([exception.Message]);
        }
    }

    public async Task<AssetOperationResult<StockQuoteRefreshDto>> RefreshQuoteAsync(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<StockQuoteRefreshDto>.NotFound("账本尚未初始化。");
        }

        if (_quoteProvider is null)
        {
            return AssetOperationResult<StockQuoteRefreshDto>.Unavailable("未配置股票行情服务。");
        }

        var position = await _ledgerRepository.GetStockPositionForUpdateAsync(
            ledger.Id,
            positionId,
            cancellationToken);

        if (position is null)
        {
            return AssetOperationResult<StockQuoteRefreshDto>.NotFound("股票持仓不存在。");
        }

        var now = _businessDateProvider.UtcNow;

        try
        {
            var quote = await _quoteProvider.GetLatestAsync(position.Market, position.Code, cancellationToken);
            var dateErrors = AssetValidation.ValidateValueDate(quote.PriceDate, _businessDateProvider.Today);

            if (dateErrors.Count > 0 || quote.Price <= 0m)
            {
                throw new QuoteUnavailableException("行情服务返回的价格或行情日期无效。");
            }

            var previousClose = quote.PreviousClose is > 0m ? quote.PreviousClose : null;
            var previousCloseDate = previousClose is null
                ? (DateOnly?)null
                : quote.PreviousCloseDate is DateOnly providedDate && providedDate < quote.PriceDate
                    ? providedDate
                    : PreviousTradingDay(quote.PriceDate);
            var latest = await _ledgerRepository.GetLatestPriceObservationAsync(
                ledger.Id,
                AssetType.Stock,
                position.Id,
                cancellationToken);
            var updated = latest is null
                || latest.Price != quote.Price
                || latest.EffectiveDate != quote.PriceDate
                || latest.Source != quote.Source;

            if (updated)
            {
                var transaction = AssetTransaction.CreateStockQuote(
                    ledger.Id,
                    position.Id,
                    quote.Price,
                    position.DisplayName,
                    quote.PriceDate,
                    Guid.NewGuid(),
                    now,
                    quote.Source);

                await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(
                    ledger,
                    transaction,
                    quote.PriceDate,
                    quote.PriceDate,
                    null,
                    cancellationToken);
            }

            position.RecordQuote(
                quote.Price,
                quote.PriceDate,
                previousClose,
                previousCloseDate,
                now);
            position.RecordQuoteSyncAttempt(now, quote.Source, null);
            ledger.Touch(now);
            await _ledgerRepository.SaveChangesAsync(cancellationToken);

            return AssetOperationResult<StockQuoteRefreshDto>.Success(new StockQuoteRefreshDto(
                position.Id,
                position.Name,
                position.Code,
                position.Market,
                updated,
                quote.Source,
                quote.Price,
                quote.PriceDate,
                position.PreviousClose,
                position.PreviousCloseDate,
                now,
                now,
                position.Value,
                ledger.CashAccount?.Balance ?? 0m));
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
                return AssetOperationResult<StockQuoteRefreshDto>.Conflict("数据已被其他操作修改，请刷新后重试。");
            }

            return AssetOperationResult<StockQuoteRefreshDto>.Unavailable(exception.Message);
        }
        catch (ConcurrencyConflictException)
        {
            _ledgerRepository.DiscardChanges();
            return AssetOperationResult<StockQuoteRefreshDto>.Conflict("数据已被其他操作修改，请刷新后重试。");
        }
    }

    public async Task<AssetOperationResult<StockQuoteRefreshBatchDto>> RefreshAllAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<StockQuoteRefreshBatchDto>.NotFound("账本尚未初始化。");
        }

        var positions = await _ledgerRepository.GetStockPositionsAsync(ledger.Id, cancellationToken);
        var items = new List<StockQuoteRefreshDto>();
        var errors = new List<string>();

        foreach (var position in positions)
        {
            var result = await RefreshQuoteAsync(position.Id, cancellationToken);

            if (result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created && result.Result is not null)
            {
                items.Add(result.Result);
            }
            else
            {
                errors.Add($"{position.DisplayName}：{string.Join(" ", result.Errors)}");
            }
        }

        return AssetOperationResult<StockQuoteRefreshBatchDto>.Success(new StockQuoteRefreshBatchDto(
            positions.Count,
            items.Count,
            positions.Count - items.Count,
            items.Count(item => item.Updated),
            items,
            errors));
    }

    private static Dictionary<Guid, decimal> CalculateTodayProfits(
        LedgerReplayData replayData,
        IReadOnlyList<StockPosition> positions)
    {
        var result = new Dictionary<Guid, decimal>();

        if (positions.Count == 0)
        {
            return result;
        }

        var calculator = new LedgerReplayCalculator();
        var facts = BusinessFact.From(replayData);
        var snapshots = new Dictionary<DateOnly, ReplayResult>();
        var stockFacts = replayData.Transactions
            .Where(transaction => !transaction.IsReversed
                && transaction.AssetType == AssetType.Stock
                && transaction.StockPositionId is not null)
            .ToList();

        ReplayResult Snapshot(DateOnly date)
        {
            if (!snapshots.TryGetValue(date, out var snapshot))
            {
                snapshot = calculator.Calculate(replayData, facts, date, currentDate: date);
                snapshots[date] = snapshot;
            }

            return snapshot;
        }

        foreach (var position in positions)
        {
            var priceDate = position.PriceDate;

            if (priceDate == default)
            {
                continue;
            }

            var baselineDate = position.PreviousCloseDate ?? PreviousTradingDay(priceDate);

            if (baselineDate >= priceDate)
            {
                continue;
            }

            var current = Snapshot(priceDate).Stocks
                .FirstOrDefault(item => item.PositionId == position.Id);

            if (current is null || !current.CanRebuild || current.Value is null)
            {
                continue;
            }

            var baseline = Snapshot(baselineDate).Stocks
                .FirstOrDefault(item => item.PositionId == position.Id);

            if (baseline is null || !baseline.CanRebuild)
            {
                continue;
            }

            var hasRestatement = stockFacts.Any(transaction =>
                transaction.StockPositionId == position.Id
                && transaction.Kind is AssetTransactionKind.StockOpening or AssetTransactionKind.StockCorrection
                && transaction.BusinessDate > baselineDate
                && transaction.BusinessDate <= priceDate);

            if (hasRestatement)
            {
                continue;
            }

            var previousClose = position.PreviousClose;

            if (previousClose is null && baseline.Quantity > 0m)
            {
                continue;
            }

            var buyCash = stockFacts
                .Where(transaction => transaction.StockPositionId == position.Id
                    && transaction.Kind == AssetTransactionKind.StockBuy
                    && transaction.BusinessDate > baselineDate
                    && transaction.BusinessDate <= priceDate)
                .Sum(transaction => transaction.Amount);
            var sellCash = stockFacts
                .Where(transaction => transaction.StockPositionId == position.Id
                    && transaction.Kind == AssetTransactionKind.StockSell
                    && transaction.BusinessDate > baselineDate
                    && transaction.BusinessDate <= priceDate)
                .Sum(transaction => transaction.Amount);
            var previousValue = (previousClose ?? 0m) * baseline.Quantity;
            result[position.Id] = decimal.Round(current.Value.Value - previousValue - buyCash + sellCash, 2);
        }

        return result;
    }

    private static DateOnly PreviousTradingDay(DateOnly date)
    {
        var candidate = date.AddDays(-1);
        var guard = 0;

        while (!IsChinaTradingDay(candidate) && guard < 20)
        {
            candidate = candidate.AddDays(-1);
            guard++;
        }

        return candidate;
    }

    private static bool IsChinaTradingDay(DateOnly date)
    {
        return TradingCalendar.Default.GetDay(date).Markets
            .Any(market => market.Market == "CN" && market.IsOpen);
    }

    private static StockPositionDto MapPosition(
        StockPosition position,
        HoldingReplay? state,
        decimal? todayProfit)
    {
        return new StockPositionDto(
            position.Id,
            position.Market,
            position.Code,
            position.Name,
            position.Currency,
            position.Shares,
            position.Cost,
            position.AverageCost,
            position.Price,
            position.PriceDate,
            position.PreviousClose,
            position.PreviousCloseDate,
            position.Value,
            position.HoldingProfit,
            position.RealizedProfit,
            todayProfit,
            todayProfit.HasValue,
            state?.CanRebuild ?? true,
            state?.Value is not null,
            position.Version,
            position.LastQuoteAttemptAt,
            position.LastQuoteSource,
            position.LastQuoteError);
    }

    private static StockMutationResultDto MapResult(
        StockPosition position,
        decimal cashBalance,
        AssetTransaction transaction,
        bool isNew = false)
    {
        return new StockMutationResultDto(
            MapPosition(position, null, null) with
            {
                HistoryConfirmed = !isNew,
                HasEffectiveQuote = true
            },
            cashBalance,
            MapActivity(transaction, position.Name));
    }

    private static StockActivityDto MapActivity(
        AssetTransaction transaction,
        IReadOnlyDictionary<Guid, string> positionNames)
    {
        var positionId = transaction.StockPositionId ?? Guid.Empty;

        return MapActivity(transaction, positionNames.GetValueOrDefault(positionId) ?? transaction.Subject ?? "股票");
    }

    private static StockActivityDto MapActivity(AssetTransaction transaction, string positionName)
    {
        var type = transaction.Kind switch
        {
            AssetTransactionKind.StockOpening => StockActivityType.Opening,
            AssetTransactionKind.StockBuy => StockActivityType.Buy,
            AssetTransactionKind.StockSell => StockActivityType.Sell,
            AssetTransactionKind.StockQuote => StockActivityType.Quote,
            AssetTransactionKind.StockCorrection => StockActivityType.Correction,
            _ => throw new InvalidOperationException("不是有效的股票记录。")
        };

        return new StockActivityDto(
            transaction.Id,
            transaction.StockPositionId ?? Guid.Empty,
            positionName,
            type,
            transaction.Quantity,
            transaction.Amount,
            transaction.CashEffect,
            transaction.Fee,
            transaction.Price,
            transaction.BusinessDate,
            transaction.Note,
            transaction.CreatedAt);
    }

    private static string Shorten(string message)
    {
        return message.Length <= 300 ? message : message[..300];
    }

    private static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }
}
