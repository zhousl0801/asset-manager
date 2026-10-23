using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Tests.Ledgers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetKeeper.Tests.Quotes;

public sealed class BatchQuoteRefreshTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FundBatch_RetriesTransientFailureAndRecordsMonitor()
    {
        var provider = new ScriptedFundProvider(
            (_, _) => throw new QuoteUnavailableException("暂时不可用"),
            (_, _) => new ExternalFundQuote("000001", 1.5m, Today, "eastmoney", Now));
        var context = await CreateAsync(provider);
        var position = await context.Funds.CreatePositionAsync(PositionRequest("000001"), CancellationToken.None);

        var run = await context.Quotes.RefreshAsync(
            QuoteRefreshScope.Funds,
            "manual",
            1,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(1, run.FundAttempted);
        Assert.Equal(1, run.FundSucceeded);
        Assert.Equal(0, run.FundFailed);
        Assert.Equal(1, run.Updated);
        Assert.Equal(2, run.Items[0].Attempts);
        Assert.Equal(2, provider.Calls);
        Assert.Equal("eastmoney", context.Repository.FundPositions.Single().LastQuoteSource);
        Assert.Same(run, context.Monitor.Snapshot().LastRun);

        var status = await context.Quotes.GetStatusAsync(CancellationToken.None);
        var item = Assert.Single(status.Items, statusItem => statusItem.AssetType == "fund");
        Assert.Equal("fresh", item.Status);
        Assert.Equal(position.Result!.Position.Id, item.PositionId);
        Assert.NotNull(status.LastRun);
        Assert.False(status.IsRunning);
    }

    [Fact]
    public async Task FundBatch_StopsAfterConfiguredRetriesAndKeepsError()
    {
        var provider = new ScriptedFundProvider(
            (_, _) => throw new QuoteUnavailableException("服务持续失败"),
            (_, _) => throw new QuoteUnavailableException("服务持续失败"),
            (_, _) => throw new QuoteUnavailableException("服务持续失败"));
        var context = await CreateAsync(provider);
        await context.Funds.CreatePositionAsync(PositionRequest("000001"), CancellationToken.None);

        var run = await context.Quotes.RefreshAsync(
            QuoteRefreshScope.Funds,
            "manual",
            2,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(1, run.FundFailed);
        Assert.Equal(0, run.FundSucceeded);
        Assert.Equal(3, run.Items[0].Attempts);
        Assert.Equal(3, provider.Calls);
        Assert.Contains("服务持续失败", context.Repository.FundPositions.Single().LastQuoteError);

        var status = await context.Quotes.GetStatusAsync(CancellationToken.None);
        Assert.Equal("failed", Assert.Single(status.Items, statusItem => statusItem.AssetType == "fund").Status);
    }

    [Fact]
    public async Task FundBatch_ContinuesAfterOneFundFailsAndFlagsMissingCode()
    {
        var provider = new ScriptedFundProvider(
            (code, _) => code == "000001"
                ? throw new QuoteUnavailableException("单只失败")
                : new ExternalFundQuote(code, 2.5m, Today, "eastmoney", Now));
        var context = await CreateAsync(provider);
        await context.Funds.CreatePositionAsync(PositionRequest("000001", "失败基金"), CancellationToken.None);
        await context.Funds.CreatePositionAsync(PositionRequest("000002", "成功基金"), CancellationToken.None);
        await context.Funds.CreatePositionAsync(PositionRequest(null, "缺代码基金"), CancellationToken.None);

        var run = await context.Quotes.RefreshAsync(
            QuoteRefreshScope.Funds,
            "manual",
            0,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(3, run.FundAttempted);
        Assert.Equal(1, run.FundSucceeded);
        Assert.Equal(2, run.FundFailed);
        Assert.Contains(run.Items, item => item.Name.StartsWith("成功基金", StringComparison.Ordinal) && item.Success && item.Updated);
        Assert.Contains(run.Items, item => item.Name.StartsWith("失败基金", StringComparison.Ordinal) && !item.Success);
        Assert.Contains(run.Items, item => item.Name.StartsWith("缺代码基金", StringComparison.Ordinal) && !item.Success);
        Assert.Equal(2, provider.Calls);

        var status = await context.Quotes.GetStatusAsync(CancellationToken.None);
        Assert.Contains(status.Items, item => item.Name.StartsWith("缺代码基金", StringComparison.Ordinal) && item.Status == "missingCode");
    }

    [Fact]
    public async Task GoldBatch_RetriesAndUpdatesReferenceQuote()
    {
        var provider = new ScriptedGoldProvider(
            _ => throw new QuoteUnavailableException("金价暂时不可用"),
            _ => new ExternalGoldQuote(910m, Today, "sina_shfe_au0", Now));
        var context = await CreateAsync(null, provider);
        await context.Gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(10m, 8000m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        var run = await context.Quotes.RefreshAsync(
            QuoteRefreshScope.Gold,
            "manual",
            1,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(1, run.GoldAttempted);
        Assert.Equal(1, run.GoldSucceeded);
        Assert.Equal(1, run.Updated);
        Assert.Equal(2, run.Items[0].Attempts);
        Assert.Equal(910m, context.Repository.GoldHolding!.Quote);

        var status = await context.Quotes.GetStatusAsync(CancellationToken.None);
        Assert.Equal("fresh", Assert.Single(status.Items).Status);
    }

    [Fact]
    public async Task Status_ReflectsSchedulerAndNoGoldHolding()
    {
        var context = await CreateAsync(null);
        context.Monitor.SetSchedule(true, 60, Now.AddMinutes(1));

        var status = await context.Quotes.GetStatusAsync(CancellationToken.None);

        Assert.True(status.SchedulerEnabled);
        Assert.Equal(60, status.IntervalSeconds);
        Assert.Equal(Now.AddMinutes(1), status.NextRunAt);
        Assert.Contains(status.Items, item => item.AssetType == "gold" && item.Status == "notRecorded");
    }

    private static CreateFundPositionRequest PositionRequest(string? code, string name = "纳指基金")
    {
        return new CreateFundPositionRequest(
            name,
            code,
            "A",
            "示例平台",
            100m,
            1000m,
            10m,
            Today.AddDays(-20),
            null,
            Guid.NewGuid(),
            HoldingDate: Today.AddDays(-20));
    }

    private static async Task<TestContext> CreateAsync(
        IFundQuoteProvider? fundProvider,
        IGoldQuoteProvider? goldProvider = null)
    {
        var repository = new FakeLedgerRepository();
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(Today.AddMonths(-2), 10000m),
            CancellationToken.None);
        var funds = new FundService(repository, dates, fundProvider);
        var gold = new GoldService(repository, dates, goldProvider);
        var monitor = new QuoteRefreshMonitor();
        var options = Options.Create(new QuoteRefreshOptions { RetryDelaySeconds = 0 });
        var quotes = new QuoteRefreshService(
            repository,
            funds,
            gold,
            dates,
            monitor,
            options,
            NullLogger<QuoteRefreshService>.Instance);

        return new TestContext(repository, funds, gold, monitor, quotes);
    }

    private sealed record TestContext(
        FakeLedgerRepository Repository,
        FundService Funds,
        GoldService Gold,
        QuoteRefreshMonitor Monitor,
        QuoteRefreshService Quotes);

    private sealed class ScriptedFundProvider : IFundQuoteProvider
    {
        private readonly Queue<Func<string, CancellationToken, ExternalFundQuote>> _responses;
        private readonly Func<string, CancellationToken, ExternalFundQuote> _fallback;

        public ScriptedFundProvider(params Func<string, CancellationToken, ExternalFundQuote>[] responses)
            : this(null, responses)
        {
        }

        private ScriptedFundProvider(
            Func<string, CancellationToken, ExternalFundQuote>? fallback,
            params Func<string, CancellationToken, ExternalFundQuote>[] responses)
        {
            _fallback = fallback ?? responses.LastOrDefault() ?? ((code, _) =>
                throw new QuoteUnavailableException("没有可用的模拟行情"));
            _responses = new Queue<Func<string, CancellationToken, ExternalFundQuote>>(responses);
        }

        public int Calls { get; private set; }

        public Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken)
        {
            Calls++;
            var response = _responses.Count > 0 ? _responses.Dequeue() : _fallback;

            try
            {
                return Task.FromResult(response(code, cancellationToken));
            }
            catch (Exception exception)
            {
                return Task.FromException<ExternalFundQuote>(exception);
            }
        }
    }

    private sealed class ScriptedGoldProvider : IGoldQuoteProvider
    {
        private readonly Queue<Func<CancellationToken, ExternalGoldQuote>> _responses;

        public ScriptedGoldProvider(params Func<CancellationToken, ExternalGoldQuote>[] responses)
        {
            _responses = new Queue<Func<CancellationToken, ExternalGoldQuote>>(responses);
        }

        public Task<ExternalGoldQuote> GetLatestAsync(CancellationToken cancellationToken)
        {
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : _ => throw new QuoteUnavailableException("没有可用的模拟黄金行情");

            try
            {
                return Task.FromResult(response(cancellationToken));
            }
            catch (Exception exception)
            {
                return Task.FromException<ExternalGoldQuote>(exception);
            }
        }
    }
}
