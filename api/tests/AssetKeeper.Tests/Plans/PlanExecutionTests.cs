using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Domain.Plans;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Plans;

public sealed class PlanExecutionTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunDue_ExecutesAllFourPlanTypesAndDoesNotDuplicate()
    {
        await using var context = await CreateAsync(10000m);
        await context.Provident.DepositAsync(new(1000m, Today.AddDays(-10), "初始公积金", Guid.NewGuid()), CancellationToken.None);
        var position = await CreatePositionAsync(context);

        await SavePlanAsync(context, AutoPlanType.SalaryIncome, 1000m, null, null, new TimeOnly(9, 0), Today);
        await SavePlanAsync(context, AutoPlanType.ProvidentDeposit, 500m, null, null, new TimeOnly(9, 0), Today);
        await SavePlanAsync(context, AutoPlanType.ProvidentWithdrawal, 200m, null, null, new TimeOnly(9, 0), Today);
        await SavePlanAsync(context, AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);

        var first = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(4, first.Completed);
        Assert.Equal(0, first.Waiting);
        Assert.Equal(0, first.Failed);
        Assert.Equal(10900m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(1300m, (await context.Provident.GetOverviewAsync(CancellationToken.None))!.Balance);
        Assert.Equal(300m, (await context.Funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);
        Assert.Equal(4, (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 100, CancellationToken.None))
            .Count(occurrence => occurrence.Status == PlanOccurrenceStatus.Completed));

        var second = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(0, second.CreatedOccurrences);
        Assert.Equal(0, second.Completed);
        Assert.Equal(10900m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Single(
            await context.Repository.GetAssetTransactionsAsync(
                context.LedgerId, AssetType.Fund, null, null, 100, CancellationToken.None),
            transaction => transaction.Kind == AssetTransactionKind.FundBuyPending);
    }

    [Fact]
    public async Task RunDue_WithInsufficientCash_LeavesFundSipWaitingAndCanBeRetried()
    {
        await using var context = await CreateAsync(100m);
        var position = await CreatePositionAsync(context);
        await SavePlanAsync(context, AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);
        await SavePlanAsync(context, AutoPlanType.ProvidentDeposit, 200m, null, null, new TimeOnly(9, 0), Today);

        var first = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, first.Completed);
        Assert.Equal(1, first.Waiting);
        Assert.Equal(100m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(200m, (await context.Provident.GetOverviewAsync(CancellationToken.None))!.Balance);
        var waiting = (await context.PlanRepo.GetPendingOccurrencesAsync(context.LedgerId, CancellationToken.None));
        Assert.Empty(waiting);
        var all = await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None);
        var sipOccurrence = all.Single(occurrence => occurrence.Type == AutoPlanType.FundSip);
        Assert.Equal(PlanOccurrenceStatus.Waiting, sipOccurrence.Status);
        Assert.Contains("现金余额不足", sipOccurrence.ErrorMessage);

        await new LedgerEntryService(context.Repository, context.Dates).CreateAsync(
            new(LedgerEntryKind.Income, "补资金", 500m, Today, null, Guid.NewGuid()), CancellationToken.None);
        var retry = await context.Plans.RetryOccurrenceAsync(
            sipOccurrence.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, retry.Status);

        var second = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, second.Completed);
        Assert.Equal(300m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(300m, (await context.Funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);
    }

    [Fact]
    public async Task Preview_ShowsExecutionOrderAndInsufficientCashWithoutWriting()
    {
        await using var context = await CreateAsync(100m);
        var position = await CreatePositionAsync(context);
        await SavePlanAsync(context, AutoPlanType.SalaryIncome, 1000m, null, null, new TimeOnly(9, 0), Today);
        await SavePlanAsync(context, AutoPlanType.FundSip, 1200m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);

        var preview = await context.Plans.PreviewAsync(new(Today, 100m), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        var rows = preview.Result!.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(AutoPlanType.SalaryIncome, rows[0].Type);
        Assert.Equal(1100m, rows[0].CashAfter);
        Assert.Equal(AutoPlanType.FundSip, rows[1].Type);
        Assert.Equal("现金不足", rows[1].Status);
        Assert.Equal(100m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Empty(await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task PauseAndResume_DoNotBackfillPausedPeriods()
    {
        await using var context = await CreateAsync(1000m);
        var plan = await SavePlanAsync(context, AutoPlanType.SalaryIncome, 100m, null, null, new TimeOnly(9, 0), Today);
        await context.Plans.PauseAsync(plan.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);

        var paused = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(0, paused.CreatedOccurrences);
        Assert.Empty(await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None));

        await context.Plans.ResumeAsync(plan.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);
        var resumed = await context.Execution.RunDueAsync(CancellationToken.None);
        var overview = await context.Plans.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(0, resumed.CreatedOccurrences);
        Assert.NotNull(overview);
        Assert.Equal(Today.AddDays(1), overview.Plans.Single().GenerateFrom);
        Assert.Equal(1000m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
    }

    [Fact]
    public async Task MainPlanAndDuplicateFundSip_AreRejected()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        await SavePlanAsync(context, AutoPlanType.SalaryIncome, 100m, null, null, new TimeOnly(9, 0), Today);
        var duplicateSalary = await context.Plans.SaveAsync(
            Request(AutoPlanType.SalaryIncome, 200m, null, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, duplicateSalary.Status);

        await SavePlanAsync(context, AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);
        var duplicateSip = await context.Plans.SaveAsync(
            Request(AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, duplicateSip.Status);
    }

    [Fact]
    public async Task PlanUpdate_DoesNotChangeExistingOccurrenceAmount()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        var plan = await SavePlanAsync(context, AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);
        await context.Execution.RunDueAsync(CancellationToken.None);
        var before = (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None)).Single();

        var update = await context.Plans.SaveAsync(
            Request(AutoPlanType.FundSip, 500m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today) with
            {
                Id = plan.Id,
                ExpectedVersion = 0
            },
            CancellationToken.None);
        var after = (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None)).Single();

        Assert.Equal(AssetOperationStatus.Success, update.Status);
        Assert.Equal(500m, update.Result!.Amount);
        Assert.Equal(300m, before.Amount);
        Assert.Equal(300m, after.Amount);
    }

    private static SavePlanRequest Request(
        AutoPlanType type,
        decimal amount,
        Guid? positionId,
        AutoPlanCycle cycle,
        TimeOnly time,
        DateOnly startDate)
    {
        return new SavePlanRequest(
            null,
            type,
            null,
            amount,
            cycle,
            cycle == AutoPlanCycle.Monthly ? Today.Day : null,
            cycle == AutoPlanCycle.Weekly ? 1 : null,
            time,
            startDate,
            positionId,
            Guid.NewGuid(),
            null);
    }

    private static async Task<PlanDto> SavePlanAsync(
        TestContext context,
        AutoPlanType type,
        decimal amount,
        Guid? positionId,
        AutoPlanCycle? cycle,
        TimeOnly time,
        DateOnly startDate)
    {
        var result = await context.Plans.SaveAsync(
            Request(type, amount, positionId, cycle ?? AutoPlanCycle.Monthly, time, startDate),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!;
    }

    private static async Task<Guid> CreatePositionAsync(TestContext context)
    {
        var result = await context.Funds.CreatePositionAsync(
            new("测试基金", "000001", "A", "测试", 100m, 1000m, 10m, Today.AddDays(-20), null, Guid.NewGuid(),
                HoldingDate: Today.AddDays(-20)),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Position.Id;
    }

    private static async Task<TestContext> CreateAsync(decimal cashBalance)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var planRepository = new PlanRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        var ledgerService = new LedgerService(repository, dates);
        await ledgerService.InitializeAsync(new InitializeLedgerRequest(Today.AddMonths(-2), cashBalance), CancellationToken.None);
        var ledgerId = db.Ledgers.Single().Id;
        var entries = new LedgerEntryService(repository, dates);
        var provident = new ProvidentFundService(repository, dates);
        var funds = new FundService(repository, dates);
        var plans = new PlanService(repository, planRepository, dates);

        return new TestContext(
            db,
            ledgerId,
            repository,
            planRepository,
            dates,
            ledgerService,
            provident,
            funds,
            plans,
            new PlanExecutionService(repository, planRepository, dates, entries, provident, funds));
    }

    private sealed record TestContext(
        AppDbContext Db,
        Guid LedgerId,
        LedgerRepository Repository,
        PlanRepository PlanRepo,
        BusinessDateProvider Dates,
        LedgerService Ledger,
        ProvidentFundService Provident,
        FundService Funds,
        PlanService Plans,
        PlanExecutionService Execution) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
