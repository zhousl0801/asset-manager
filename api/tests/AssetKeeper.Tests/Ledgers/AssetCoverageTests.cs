using AssetKeeper.Tests.Quotes;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Ledgers;

public sealed class AssetCoverageTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateCoverage_MarksUnheldAssetsAndReportsCompleteCoverage()
    {
        await using var context = await CreateAsync(includeFund: true);

        var result = await context.Ledger.UpdateCoverageAsync(
            new UpdateAssetCoverageRequest(false, true, true, true),
            CancellationToken.None);

        Assert.Equal(AssetCoverageStatus.Updated, result.Status);
        Assert.NotNull(result.Overview);
        Assert.True(result.Overview.CoverageComplete);
        Assert.True(result.Overview.ProvidentFund.NotHeld);
        Assert.True(result.Overview.Gold.NotHeld);
        Assert.True(result.Overview.Stock.NotHeld);
        Assert.False(result.Overview.Fund.NotHeld);
        Assert.Equal(0m, result.Overview.ProvidentFund.Amount);
        Assert.Equal(2000m, result.Overview.TotalAssets);
    }

    [Fact]
    public async Task UpdateCoverage_RejectsNotHeldWhenHoldingsExist()
    {
        await using var context = await CreateAsync(includeFund: true);

        var result = await context.Ledger.UpdateCoverageAsync(
            new UpdateAssetCoverageRequest(true, false, false, false),
            CancellationToken.None);

        Assert.Equal(AssetCoverageStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("基金已有持仓"));
    }

    private static async Task<TestContext> CreateAsync(bool includeFund)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledger = new LedgerService(repository, dates);
        await ledger.InitializeAsync(new InitializeLedgerRequest(Today, 1000m), CancellationToken.None);

        if (includeFund)
        {
            await TestFundQuotes.CreatePositionAsync(repository, dates,
                new CreateFundPositionRequest(
                    "测试基金",
                    "000001",
                    "A",
                    "测试平台",
                    100m,
                    1000m,
                    10m,
                    Today,
                    null,
                    Guid.NewGuid(),
                    HoldingDate: Today),
                CancellationToken.None);
        }

        return new TestContext(db, ledger);
    }

    private sealed record TestContext(AppDbContext Db, LedgerService Ledger) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
