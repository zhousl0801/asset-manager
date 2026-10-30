using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Api.Quotes;

public sealed class QuoteRefreshSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<QuoteRefreshOptions> _options;
    private readonly QuoteRefreshMonitor _monitor;
    private readonly BusinessDateProvider _dates;
    private readonly ILogger<QuoteRefreshSchedulerService> _logger;

    public QuoteRefreshSchedulerService(
        IServiceScopeFactory scopeFactory,
        IOptions<QuoteRefreshOptions> options,
        QuoteRefreshMonitor monitor,
        BusinessDateProvider dates,
        ILogger<QuoteRefreshSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _monitor = monitor;
        _dates = dates;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        var intervalSeconds = Math.Max(5, options.IntervalSeconds);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        if (!options.Enabled)
        {
            _monitor.SetSchedule(false, intervalSeconds, null);
            _logger.LogInformation("行情定时刷新未启用。");
            return;
        }

        if (!options.IncludeFunds && !options.IncludeGold && !options.IncludeStocks)
        {
            _monitor.SetSchedule(true, intervalSeconds, null);
            _logger.LogWarning("行情定时刷新已启用，但基金、积存金和股票均被关闭，任务不会执行。");
            return;
        }

        using var timer = new PeriodicTimer(interval);
        _logger.LogInformation(
            "行情定时刷新已启动：间隔 {IntervalSeconds} 秒，启动时刷新 {RunOnStartup}，重试 {RetryCount} 次。",
            intervalSeconds,
            options.RunOnStartup,
            options.RetryCount);

        try
        {
            if (!options.RunOnStartup)
            {
                SetNextRun(intervalSeconds, interval);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }

            do
            {
                await RunAsync(stoppingToken);
                SetNextRun(intervalSeconds, interval);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _monitor.SetSchedule(false, intervalSeconds, null);
            _logger.LogInformation("行情定时刷新已停止。");
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<QuoteRefreshService>();
            var options = _options.Value;
            var enabled = new[]
            {
                options.IncludeFunds ? QuoteRefreshScope.Funds : (QuoteRefreshScope?)null,
                options.IncludeGold ? QuoteRefreshScope.Gold : null,
                options.IncludeStocks ? QuoteRefreshScope.Stocks : null
            }.Where(scope => scope is not null).Select(scope => scope!.Value).ToList();
            var refreshScope = enabled.Count == 1 ? enabled[0] : QuoteRefreshScope.All;
            var result = await service.RefreshAsync(
                refreshScope,
                "scheduled",
                service.RetryCount,
                service.RetryDelay,
                stoppingToken);

            if (result is not null)
            {
                _logger.LogInformation(
                    "定时行情刷新结束：基金 {FundSucceeded}/{FundAttempted}，积存金 {GoldSucceeded}/{GoldAttempted}，股票 {StockSucceeded}/{StockAttempted}，更新 {Updated} 项。",
                    result.FundSucceeded,
                    result.FundAttempted,
                    result.GoldSucceeded,
                    result.GoldAttempted,
                    result.StockSucceeded,
                    result.StockAttempted,
                    result.Updated);
            }
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "定时行情刷新跳过：已有刷新任务在运行。");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "定时行情刷新执行失败。");
        }
    }

    private void SetNextRun(int intervalSeconds, TimeSpan interval)
    {
        _monitor.SetSchedule(true, intervalSeconds, _dates.UtcNow + interval);
    }
}
