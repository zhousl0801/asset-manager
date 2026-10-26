using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Application.Quotes;

public sealed class QuoteRefreshService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly FundService _fundService;
    private readonly GoldService _goldService;
    private readonly BusinessDateProvider _dates;
    private readonly QuoteRefreshMonitor _monitor;
    private readonly QuoteRefreshOptions _options;
    private readonly ILogger<QuoteRefreshService> _logger;

    public QuoteRefreshService(
        ILedgerRepository ledgerRepository,
        FundService fundService,
        GoldService goldService,
        BusinessDateProvider dates,
        QuoteRefreshMonitor monitor,
        IOptions<QuoteRefreshOptions> options,
        ILogger<QuoteRefreshService> logger)
    {
        _ledgerRepository = ledgerRepository;
        _fundService = fundService;
        _goldService = goldService;
        _dates = dates;
        _monitor = monitor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QuoteRefreshRunDto?> RefreshAsync(
        QuoteRefreshScope scope,
        string trigger,
        int retryCount,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        if (!_monitor.TryBegin(trigger))
        {
            throw new InvalidOperationException("行情刷新正在进行中，请稍后重试。");
        }

        var startedAt = _dates.UtcNow;
        var items = new List<QuoteRefreshItemDto>();
        var errors = new List<string>();

        try
        {
            IReadOnlyList<FundPosition> positions = scope is QuoteRefreshScope.All or QuoteRefreshScope.Funds
                ? await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken)
                : [];
            var gold = scope is QuoteRefreshScope.All or QuoteRefreshScope.Gold
                ? await _ledgerRepository.GetGoldHoldingForUpdateAsync(ledger.Id, cancellationToken)
                : null;

            _logger.LogInformation(
                "行情批量刷新开始：范围 {Scope}，触发 {Trigger}，基金 {FundCount} 只，积存金 {HasGold}。",
                scope,
                trigger,
                positions.Count,
                gold is not null);

            foreach (var position in positions)
            {
                var (result, attempts) = await RefreshFundWithRetryAsync(
                    position,
                    retryCount,
                    retryDelay,
                    cancellationToken);
                var success = result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created;
                var error = success ? null : string.Join(" ", result.Errors);
                var item = new QuoteRefreshItemDto(
                    "fund",
                    position.Id,
                    position.DisplayName,
                    position.Code,
                    position.Platform,
                    success,
                    success && result.Result?.Updated == true,
                    attempts,
                    success ? result.Result?.Source : null,
                    success ? result.Result?.NetValue : null,
                    success ? result.Result?.EffectiveDate : null,
                    success ? result.Result?.LastAttemptAt : _dates.UtcNow,
                    error);
                items.Add(item);

                if (!success)
                {
                    errors.Add($"{position.DisplayName}：{error}");
                }
            }

            if (gold is not null)
            {
                var (result, attempts) = await RefreshGoldWithRetryAsync(
                    retryCount,
                    retryDelay,
                    cancellationToken);
                var success = result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created;
                var error = success ? null : string.Join(" ", result.Errors);
                var item = new QuoteRefreshItemDto(
                    "gold",
                    null,
                    "工行积存金报价",
                    null,
                    null,
                    success,
                    success && result.Result?.Updated == true,
                    attempts,
                    success ? result.Result?.Source : null,
                    success ? result.Result?.Quote : null,
                    success ? result.Result?.QuoteDate : null,
                    success ? result.Result?.LastAttemptAt : _dates.UtcNow,
                    error);
                items.Add(item);

                if (!success)
                {
                    errors.Add($"工行积存金报价：{error}");
                }
            }

            var completedAt = _dates.UtcNow;
            var run = BuildRun(trigger, scope, startedAt, completedAt, items, errors);
            _monitor.Complete(run);

            if (run.FundFailed + run.GoldFailed > 0)
            {
                _logger.LogWarning(
                    "行情批量刷新完成，存在失败：基金 {FundSuccess}/{FundAttempted} 成功，积存金 {GoldSuccess}/{GoldAttempted} 成功，更新 {Updated} 项，耗时 {DurationMs} 毫秒。",
                    run.FundSucceeded,
                    run.FundAttempted,
                    run.GoldSucceeded,
                    run.GoldAttempted,
                    run.Updated,
                    run.DurationMs);
            }
            else
            {
                _logger.LogInformation(
                    "行情批量刷新完成：基金 {FundCount} 只，积存金 {GoldAttempted} 项，更新 {Updated} 项，耗时 {DurationMs} 毫秒。",
                    run.FundAttempted,
                    run.GoldAttempted,
                    run.Updated,
                    run.DurationMs);
            }

            return run;
        }
        catch (Exception exception)
        {
            var completedAt = _dates.UtcNow;
            var run = BuildRun(
                trigger,
                scope,
                startedAt,
                completedAt,
                items,
                errors.Concat([exception.Message]).ToList());
            _monitor.Complete(run);

            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogError(exception, "行情批量刷新失败：范围 {Scope}，触发 {Trigger}。", scope, trigger);
            throw;
        }
    }

    public async Task<QuoteStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);
        var items = new List<QuoteStatusItemDto>();

        if (ledger is not null)
        {
            var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
            var gold = await _ledgerRepository.GetGoldHoldingAsync(ledger.Id, cancellationToken);

            items.AddRange(positions.Select(position => new QuoteStatusItemDto(
                "fund",
                position.Id,
                position.DisplayName,
                position.Code,
                position.Platform,
                position.NetValue,
                position.NetValueDate,
                position.LastQuoteAttemptAt,
                position.LastQuoteSource,
                position.LastQuoteError,
                FundStatus(position))));

            items.Add(gold is null
                ? new QuoteStatusItemDto(
                    "gold",
                    null,
                    "工行积存金报价",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "notRecorded")
                : new QuoteStatusItemDto(
                    "gold",
                    null,
                    "工行积存金报价",
                    null,
                    null,
                    gold.Quote,
                    gold.QuoteDate,
                    gold.LastQuoteAttemptAt,
                    gold.LastQuoteSource,
                    gold.LastQuoteError,
                    GoldStatus(gold)));
        }

        var snapshot = _monitor.Snapshot();
        var interval = _options.IntervalSeconds > 0 ? _options.IntervalSeconds : 900;

        return new QuoteStatusDto(
            snapshot.SchedulerEnabled,
            snapshot.IntervalSeconds > 0 ? snapshot.IntervalSeconds : interval,
            snapshot.NextRunAt,
            snapshot.IsRunning,
            snapshot.LastRun,
            items,
            _dates.UtcNow);
    }

    public int RetryCount => Math.Clamp(_options.RetryCount, 0, 5);

    public TimeSpan RetryDelay => TimeSpan.FromSeconds(Math.Clamp(_options.RetryDelaySeconds, 0, 60));

    private async Task<(AssetOperationResult<FundQuoteRefreshDto> Result, int Attempts)> RefreshFundWithRetryAsync(
        FundPosition position,
        int retryCount,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxAttempts = Math.Clamp(retryCount, 0, 5) + 1;

        while (true)
        {
            attempts++;
            AssetOperationResult<FundQuoteRefreshDto> result;

            try
            {
                result = await _fundService.RefreshQuoteAsync(position.Id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = AssetOperationResult<FundQuoteRefreshDto>.Unavailable(exception.Message);
            }

            if (result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created)
            {
                return (result, attempts);
            }

            var error = string.Join(" ", result.Errors);

            if ((result.Status is AssetOperationStatus.Unavailable or AssetOperationStatus.Conflict)
                && attempts < maxAttempts)
            {
                _logger.LogWarning(
                    "基金「{Name}」第 {Attempt}/{MaxAttempts} 次刷新失败，将重试：{Error}",
                    position.DisplayName,
                    attempts,
                    maxAttempts,
                    error);

                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }

                continue;
            }

            _logger.LogWarning(
                "基金「{Name}」刷新失败，已尝试 {Attempt} 次：{Error}",
                position.DisplayName,
                attempts,
                error);

            return (result, attempts);
        }
    }

    private async Task<(AssetOperationResult<GoldQuoteRefreshDto> Result, int Attempts)> RefreshGoldWithRetryAsync(
        int retryCount,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxAttempts = Math.Clamp(retryCount, 0, 5) + 1;

        while (true)
        {
            attempts++;
            AssetOperationResult<GoldQuoteRefreshDto> result;

            try
            {
                result = await _goldService.RefreshQuoteAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = AssetOperationResult<GoldQuoteRefreshDto>.Unavailable(exception.Message);
            }

            if (result.Status is AssetOperationStatus.Success or AssetOperationStatus.Created)
            {
                return (result, attempts);
            }

            var error = string.Join(" ", result.Errors);

            if ((result.Status is AssetOperationStatus.Unavailable or AssetOperationStatus.Conflict)
                && attempts < maxAttempts)
            {
                _logger.LogWarning(
                    "工行积存金报价第 {Attempt}/{MaxAttempts} 次刷新失败，将重试：{Error}",
                    attempts,
                    maxAttempts,
                    error);

                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }

                continue;
            }

            _logger.LogWarning(
                "工行积存金报价刷新失败，已尝试 {Attempt} 次：{Error}",
                attempts,
                error);

            return (result, attempts);
        }
    }

    private static QuoteRefreshRunDto BuildRun(
        string trigger,
        QuoteRefreshScope scope,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<QuoteRefreshItemDto> items,
        IReadOnlyList<string> errors)
    {
        var fundItems = items.Where(item => item.AssetType == "fund").ToList();
        var goldItems = items.Where(item => item.AssetType == "gold").ToList();

        return new QuoteRefreshRunDto(
            trigger,
            scope,
            startedAt,
            completedAt,
            Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds),
            fundItems.Count,
            fundItems.Count(item => item.Success),
            fundItems.Count(item => !item.Success),
            goldItems.Count,
            goldItems.Count(item => item.Success),
            goldItems.Count(item => !item.Success),
            items.Count(item => item.Updated),
            items,
            errors.Distinct().ToList());
    }

    private string FundStatus(FundPosition position)
    {
        if (string.IsNullOrWhiteSpace(position.Code))
        {
            return "missingCode";
        }

        if (!string.IsNullOrWhiteSpace(position.LastQuoteError))
        {
            return "failed";
        }

        if (position.LastQuoteAttemptAt is null)
        {
            return "never";
        }

        return position.NetValueDate < _dates.Today.AddDays(-7) ? "stale" : "fresh";
    }

    private string GoldStatus(GoldHolding holding)
    {
        if (!string.IsNullOrWhiteSpace(holding.LastQuoteError))
        {
            return "failed";
        }

        if (holding.LastQuoteAttemptAt is null)
        {
            return "never";
        }

        return holding.QuoteDate < _dates.Today.AddDays(-7) ? "stale" : "fresh";
    }
}
