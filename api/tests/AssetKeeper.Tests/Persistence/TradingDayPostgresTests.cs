using AssetKeeper.Tests.Quotes;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Plans;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Persistence;

[Collection("PlanExecution")]
public sealed class TradingDayPostgresTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly BusinessDateProvider Dates = new(new FixedTimeProvider(new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero)));

    [PostgresFact]
    public async Task ConcurrentSipSaves_KeepOnePlanAndExecutionPersistsOnePendingBuy()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var target = new FundPlanTarget("基金", "000001", "C");
        await using (var seed = database.CreateContext())
        {
            var repository = new LedgerRepository(seed);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 1000m), CancellationToken.None);
            var funds = new FundService(repository, Dates);
            await TestFundQuotes.CreatePositionAsync(repository, Dates, new(target.Name, target.Code, target.ShareClass, "来源一", 10m, 100m,
                10m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
            await TestFundQuotes.CreatePositionAsync(repository, Dates, new("别名", target.Code, target.ShareClass, "来源二", 5m, 50m,
                10m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
        }

        await using (var first = database.CreateContext())
        await using (var second = database.CreateContext())
        {
            await first.Ledgers.LoadAsync();
            await second.Ledgers.LoadAsync();
            var request = new SavePlanRequest(null, AutoPlanType.FundSip, null, 100m, AutoPlanCycle.TradingDay,
                null, null, new TimeOnly(9, 0), Today, null, Guid.NewGuid(), null, target);
            var results = await Task.WhenAll(
                new PlanService(new LedgerRepository(first), new PlanRepository(first), Dates).SaveAsync(request, CancellationToken.None),
                new PlanService(new LedgerRepository(second), new PlanRepository(second), Dates).SaveAsync(request with { RequestId = Guid.NewGuid() }, CancellationToken.None));
            Assert.Single(results, result => result.Status == AssetOperationStatus.Created);
            Assert.Single(results, result => result.Status == AssetOperationStatus.Conflict);
        }

        var nextDates = new BusinessDateProvider(new FixedTimeProvider(new(2026, 10, 22, 2, 0, 0, TimeSpan.Zero)));
        await using (var run = database.CreateContext())
        {
            var repository = new LedgerRepository(run);
            var execution = new PlanExecutionService(repository, new PlanRepository(run), nextDates,
                new LedgerEntryService(repository, nextDates), new ProvidentFundService(repository, nextDates), new FundService(repository, nextDates));
            Assert.Equal(1, (await execution.RunDueAsync(CancellationToken.None)).Completed);
            Assert.Equal(0, (await execution.RunDueAsync(CancellationToken.None)).Completed);
        }

        await using var readback = database.CreateContext();
        var plan = await readback.AutoPlans.SingleAsync();
        Assert.Equal(AutoPlanCycle.TradingDay, plan.Cycle);
        Assert.Equal(Today.AddDays(1), plan.StartDate);
        Assert.Equal("T2026-10-22", (await readback.PlanOccurrences.SingleAsync()).PeriodKey);
        var overview = (await new FundService(new LedgerRepository(readback), nextDates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(2, overview.Positions.Count);
        Assert.Equal(15m, Assert.Single(Assert.Single(overview.Funds!).Classes).Shares);
        Assert.Equal(100m, overview.PendingAmount);
        Assert.Equal(900m, (await readback.CashAccounts.SingleAsync()).Balance);
    }
}
