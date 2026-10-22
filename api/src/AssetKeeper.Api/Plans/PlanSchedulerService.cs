using AssetKeeper.Application.Plans;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Api.Plans;

public sealed class PlanSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PlanSchedulerOptions> _options;
    private readonly ILogger<PlanSchedulerService> _logger;

    public PlanSchedulerService(
        IServiceScopeFactory scopeFactory,
        IOptions<PlanSchedulerOptions> options,
        ILogger<PlanSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var execution = scope.ServiceProvider.GetRequiredService<PlanExecutionService>();
            var result = await execution.RunDueAsync(stoppingToken);

            if (result.CreatedOccurrences + result.Completed + result.Waiting + result.Failed > 0)
            {
                _logger.LogInformation(
                    "计划任务执行完成：新建 {Created}，已记账 {Completed}，待处理 {Waiting}，失败 {Failed}。",
                    result.CreatedOccurrences,
                    result.Completed,
                    result.Waiting,
                    result.Failed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "计划任务执行失败。");
        }
    }
}
