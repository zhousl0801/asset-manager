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

[Collection("PlanExecution")]
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
            new(LedgerEntryKind.Income, "其他收入", 500m, Today, null, Guid.NewGuid()), CancellationToken.None);
        var retry = await context.Plans.RetryOccurrenceAsync(
            sipOccurrence.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, retry.Status);

        var second = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, second.Completed);
        Assert.Equal(300m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(300m, (await context.Funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);
    }

    [Fact]
    public async Task RecordOccurrence_CompletesWaitingOccurrenceAfterCashTopUp()
    {
        await using var context = await CreateAsync(100m);
        var position = await CreatePositionAsync(context);
        await SavePlanAsync(context, AutoPlanType.FundSip, 300m, position, AutoPlanCycle.Monthly, new TimeOnly(9, 0), Today);

        var first = await context.Execution.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, first.Waiting);
        var occurrence = (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None))
            .Single();

        var insufficient = await context.Execution.RecordOccurrenceAsync(
            occurrence.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, insufficient.Status);
        Assert.False(insufficient.Result!.Recorded);
        Assert.Contains("现金余额不足", insufficient.Result.Error);
        Assert.Equal(PlanOccurrenceStatus.Waiting, insufficient.Result.Occurrence.Status);
        Assert.Equal(100m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);

        await new LedgerEntryService(context.Repository, context.Dates).CreateAsync(
            new(LedgerEntryKind.Income, "其他收入", 500m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var request = new PlanActionRequest(Guid.NewGuid());
        var recorded = await context.Execution.RecordOccurrenceAsync(occurrence.Id, request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, recorded.Status);
        Assert.True(recorded.Result!.Recorded);
        Assert.Null(recorded.Result.Error);
        Assert.Equal(PlanOccurrenceStatus.Completed, recorded.Result.Occurrence.Status);
        Assert.Equal(300m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(300m, (await context.Funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);

        var replay = await context.Execution.RecordOccurrenceAsync(occurrence.Id, request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, replay.Status);
        Assert.True(replay.Result!.Recorded);
        Assert.Equal(300m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(300m, (await context.Funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);

        var repeated = await context.Execution.RecordOccurrenceAsync(
            occurrence.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, repeated.Status);
    }

    [Fact]
    public async Task RecordOccurrence_WithCompletedOccurrence_ReturnsInvalid()
    {
        await using var context = await CreateAsync(1000m);
        await SavePlanAsync(context, AutoPlanType.SalaryIncome, 100m, null, null, new TimeOnly(9, 0), Today);
        await context.Execution.RunDueAsync(CancellationToken.None);
        var occurrence = (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 10, CancellationToken.None))
            .Single();

        var completed = await context.Execution.RecordOccurrenceAsync(
            occurrence.Id, new PlanActionRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, completed.Status);
        Assert.Contains(completed.Errors, error => error.Contains("不能补记"));
        Assert.Equal(1100m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
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

        Assert.Equal(AssetOperationStatus.Conflict, duplicateSip.Status);
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

    [Fact]
    public async Task TradingDaySave_TargetsShareClassAndStartsAfterCreationWithoutCashChange()
    {
        await using var context = await CreateAsync(1000m);
        await CreatePositionAsync(context);
        var request = Request(AutoPlanType.FundSip, 100m, null, AutoPlanCycle.TradingDay, new TimeOnly(9, 0), Today)
            with
        { FundTarget = new("测试基金", "000001", "A") };

        var saved = await context.Plans.SaveAsync(request, CancellationToken.None);
        var retried = await context.Plans.SaveAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, saved.Status);
        Assert.Equal(Today.AddDays(1), saved.Result!.StartDate);
        Assert.Equal(saved.Result.Id, retried.Result!.Id);
        Assert.Equal("000001", saved.Result.FundTarget!.Code);
        Assert.Equal(1000m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(0, (await context.Execution.RunDueAsync(CancellationToken.None)).CreatedOccurrences);
        Assert.Single((await context.Plans.GetOverviewAsync(CancellationToken.None))!.Plans);
    }

    [Fact]
    public async Task TradingDayRun_UsesPreviewCalendarAndRetriesWithoutDuplicateBuy()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        await SavePlanAsync(context, AutoPlanType.FundSip, 100m, position, AutoPlanCycle.TradingDay, new TimeOnly(9, 0), Today);
        var nextDates = new BusinessDateProvider(new FixedTimeProvider(Now.AddDays(1)));
        var plans = new PlanService(context.Repository, context.PlanRepo, nextDates);
        var preview = await plans.PreviewAsync(new(Today.AddDays(1), null), CancellationToken.None);
        var funds = new FundService(context.Repository, nextDates);
        var execution = new PlanExecutionService(context.Repository, context.PlanRepo, nextDates,
            new LedgerEntryService(context.Repository, nextDates), new ProvidentFundService(context.Repository, nextDates), funds);

        Assert.True(preview.Result!.CalendarDay!.IsTradingDay);
        Assert.Single(preview.Result.Rows);
        Assert.Equal(1, (await execution.RunDueAsync(CancellationToken.None)).Completed);
        Assert.Equal(0, (await execution.RunDueAsync(CancellationToken.None)).Completed);
        Assert.Equal(900m, (await context.Ledger.GetOverviewAsync(CancellationToken.None))!.Cash.Amount);
        Assert.Equal(100m, (await funds.GetOverviewAsync(CancellationToken.None))!.PendingAmount);
        Assert.Equal("T2026-10-22", (await context.PlanRepo.GetOccurrencesAsync(context.LedgerId, null, null, 100, CancellationToken.None)).Single().PeriodKey);
    }

    [Fact]
    public async Task DuplicateSip_AcrossSourcesEditAndResume_ReturnsConflict()
    {
        await using var context = await CreateAsync(1000m);
        var firstPosition = await CreatePositionAsync(context);
        var alias = await context.Funds.CreatePositionAsync(
            new("基金别名", "000001", "A", "另一来源", 1m, 10m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
        var first = await SavePlanAsync(context, AutoPlanType.FundSip, 100m, firstPosition, AutoPlanCycle.TradingDay, new TimeOnly(9, 0), Today);
        var request = Request(AutoPlanType.FundSip, 100m, alias.Result!.Position.Id, AutoPlanCycle.TradingDay, new TimeOnly(9, 0), Today);
        Assert.Equal(AssetOperationStatus.Conflict, (await context.Plans.SaveAsync(request, CancellationToken.None)).Status);

        var second = await SavePlanAsync(context, AutoPlanType.FundSip, 200m, alias.Result.Position.Id, AutoPlanCycle.TradingDay, new TimeOnly(9, 0), Today);
        Assert.Equal(AssetOperationStatus.Conflict, (await context.Plans.SaveAsync(request with { Id = second.Id, RequestId = Guid.NewGuid() }, CancellationToken.None)).Status);
        await context.Plans.PauseAsync(second.Id, new(Guid.NewGuid()), CancellationToken.None);
        await context.Plans.SaveAsync(request with { Id = second.Id, RequestId = Guid.NewGuid() }, CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Conflict, (await context.Plans.ResumeAsync(second.Id, new(Guid.NewGuid()), CancellationToken.None)).Status);
        Assert.False((await context.Plans.GetOverviewAsync(CancellationToken.None))!.Plans.Single(plan => plan.Id == second.Id).IsActive);
        Assert.True((await context.Plans.GetOverviewAsync(CancellationToken.None))!.Plans.Single(plan => plan.Id == first.Id).IsActive);
    }

    [Fact]
    public async Task TradingDaySave_SkipsHolidayAtRequestedStart()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        var saved = await SavePlanAsync(context, AutoPlanType.FundSip, 100m, position, AutoPlanCycle.TradingDay,
            new TimeOnly(9, 0), new DateOnly(2026, 11, 26));
        Assert.Equal(new DateOnly(2026, 11, 27), saved.StartDate);
        Assert.Equal(saved.StartDate, saved.NextDueDate);
    }

    [Fact]
    public async Task SwitchToTradingDay_DoesNotBackfillDatesBeforeEdit()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        var original = await SavePlanAsync(context, AutoPlanType.FundSip, 100m, position, AutoPlanCycle.Monthly,
            new TimeOnly(9, 0), Today.AddDays(-20));
        var changed = await context.Plans.SaveAsync(Request(AutoPlanType.FundSip, 100m, position,
            AutoPlanCycle.TradingDay, new TimeOnly(9, 0), original.StartDate) with
        { Id = original.Id }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, changed.Status);
        Assert.Equal(0, (await context.Execution.RunDueAsync(CancellationToken.None)).CreatedOccurrences);
        Assert.True(changed.Result!.GenerateFrom > Today);
    }

    [Fact]
    public async Task TradingDayEdit_BeforeDueTimePreservesTodaysFuturePeriod()
    {
        await using var context = await CreateAsync(1000m);
        var position = await CreatePositionAsync(context);
        var plan = AutoPlan.Create(context.LedgerId, AutoPlanType.FundSip, "已有定投", 100m,
            AutoPlanCycle.TradingDay, null, null, new TimeOnly(11, 0), Today.AddDays(-1), position, Now.AddDays(-2));
        await context.PlanRepo.AddPlanAsync(plan, CancellationToken.None);
        await context.PlanRepo.SaveChangesAsync(CancellationToken.None);
        var changed = await context.Plans.SaveAsync(Request(AutoPlanType.FundSip, 200m, position,
            AutoPlanCycle.TradingDay, new TimeOnly(11, 0), plan.StartDate) with
        { Id = plan.Id }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, changed.Status);
        Assert.Equal(Today, changed.Result!.GenerateFrom);
        Assert.Equal(Today, changed.Result.NextDueDate);
        Assert.Contains(Today, changed.Result.UpcomingDates!);
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
