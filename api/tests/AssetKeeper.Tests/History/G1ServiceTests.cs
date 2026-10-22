using System.Text.Json;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.History;

public sealed class G1ServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BackdatedBuy_RecalculatesLaterSaleCost()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var opening = await funds.CreatePositionAsync(Position(Today.AddDays(-20), 10m, Today.AddDays(-20)), CancellationToken.None);
        var id = opening.Result!.Position.Id;
        await funds.SellAsync(new(id, 50m, 1000m, Today.AddDays(-6), null, Guid.NewGuid()), CancellationToken.None);

        var pending = await funds.BuyAsync(new(id, 2000m, Today.AddDays(-11), null, Guid.NewGuid()), CancellationToken.None);
        var result = await funds.ConfirmBuyAsync(pending.Result!.Activity.Id,
            new ConfirmFundBuyRequest(100m, 20m, Today.AddDays(-11), 0m, Today.AddDays(-11), null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        var position = (await funds.GetOverviewAsync(CancellationToken.None))!.Positions.Single();
        Assert.Equal(2250m, position.Cost);
        Assert.Equal(250m, position.RealizedProfit);
        Assert.Equal(150m, position.Shares);
    }

    [Fact]
    public async Task HoldingDate_IsIndependentOfQuoteDate()
    {
        await using var context = await CreateAsync();
        var result = await new FundService(context.Repository, context.Dates).CreatePositionAsync(Position(Today.AddDays(-20), 15m, Today), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        Assert.Equal(Today.AddDays(-20), result.Result!.Activity.BusinessDate);
        Assert.Equal(Today, result.Result.Position.NetValueDate);
    }

    [Fact]
    public async Task LinkedMissingExpense_ReplacesCashAdjustmentInsteadOfDeductingTwice()
    {
        await using var context = await CreateAsync(1000m);
        var cash = new CashService(context.Repository, context.Dates);
        var adjustment = await cash.ReconcileAsync(new(950m, "漏记", Guid.NewGuid(), 0), CancellationToken.None);
        var request = JsonSerializer.Deserialize<CreateLedgerEntryRequest>(JsonSerializer.Serialize(new
        {
            Kind = LedgerEntryKind.Expense,
            Category = "餐饮",
            Amount = 50m,
            BusinessDate = Today.AddDays(-1),
            Note = "找到漏记业务",
            RequestId = Guid.NewGuid(),
            RelatedCashAdjustmentId = adjustment.Result!.Adjustment!.Id
        }))!;

        var result = await new LedgerEntryService(context.Repository, context.Dates).CreateAsync(request, CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Created, result.Status);
        var overview = (await cash.GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(950m, overview.Balance);
        Assert.Equal(0m, overview.AdjustmentTotal);
        Assert.Equal(50m, overview.Outflow);
    }

    [Fact]
    public async Task Preview_IsReadOnlyAndChangesRequireSameContentAndVersion()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var opening = await funds.CreatePositionAsync(Position(Today.AddDays(-20), 10m, Today.AddDays(-20)), CancellationToken.None);
        await funds.SellAsync(new(opening.Result!.Position.Id, 50m, 1000m, Today.AddDays(-6), null, Guid.NewGuid()), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundOpening);
        var request = Change(history, fact) with { Amount = 2000m };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(500m, preview.Result!.Before.Funds.Single().Cost);
        Assert.Equal(1000m, preview.Result.After.Funds.Single().Cost);
        Assert.Equal(0m, preview.Result.After.Funds.Single().RealizedProfit);
        Assert.Equal(500m, (await funds.GetOverviewAsync(CancellationToken.None))!.Positions.Single().Cost);
        Assert.DoesNotContain(context.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Modified or EntityState.Added);
        var tampered = await service.ChangeAsync(new(request with { Amount = 3000m }, preview.Result.PreviewToken), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Conflict, tampered.Status);
        var confirmed = new ConfirmHistoryChangeRequest(request, preview.Result.PreviewToken);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(confirmed, CancellationToken.None)).Status);
        Assert.Equal(1000m, (await funds.GetOverviewAsync(CancellationToken.None))!.Positions.Single().Cost);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(confirmed, CancellationToken.None)).Status);
        Assert.Single((await service.GetAsync(null, CancellationToken.None))!.Audit);
        using var audit = JsonDocument.Parse((await service.GetAsync(null, CancellationToken.None))!.Audit.Single().BeforeJson!);
        Assert.Equal("测试更正", audit.RootElement.GetProperty("request").GetProperty("change").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task StalePreview_IsRejectedAfterAnotherWrite()
    {
        await using var context = await CreateAsync();
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var request = Change(history, history.CashOpening) with { Amount = 12000m };
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        await new ProvidentFundService(context.Repository, context.Dates).DepositAsync(new(10m, Today, null, Guid.NewGuid()), CancellationToken.None);
        var result = await service.ChangeAsync(new(request, preview.Result!.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Conflict, result.Status);
        Assert.Equal(10000m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task ReverseOpening_WithLaterSaleIsRejectedWithoutDirtyEntities()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var opening = await funds.CreatePositionAsync(Position(Today.AddDays(-20), 10m, Today.AddDays(-20)), CancellationToken.None);
        await funds.SellAsync(new(opening.Result!.Position.Id, 50m, 1000m, Today.AddDays(-6), null, Guid.NewGuid()), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundOpening);
        var preview = await service.PreviewAsync(Change(history, fact) with { Action = "reverse" }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("超卖"));
        Assert.False((await context.Db.AssetTransactions.SingleAsync(item => item.Id == fact.Id)).IsReversed);
    }

    [Fact]
    public async Task SameDayOrder_CanBeChangedExplicitlyButCannotSpendLaterIncome()
    {
        await using var context = await CreateAsync(0m);
        var entries = new LedgerEntryService(context.Repository, context.Dates);
        var income = await entries.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today, null, Guid.NewGuid()), CancellationToken.None);
        await entries.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 50m, Today, null, Guid.NewGuid()), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Id == income.Result!.Entry.Id);
        var preview = await service.PreviewAsync(Change(history, fact) with { Sequence = 100 }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("现金余额不足"));
        Assert.Equal(50m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task InvalidLinkedExpense_RollsBackAndDoesNotPoisonNextWrite()
    {
        await using var context = await CreateAsync(1000m);
        var cash = new CashService(context.Repository, context.Dates);
        var adjustment = await cash.ReconcileAsync(new(950m, "漏记", Guid.NewGuid()), CancellationToken.None);
        var entries = new LedgerEntryService(context.Repository, context.Dates);
        var invalid = await entries.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 60m, Today.AddDays(-1), null, Guid.NewGuid(), adjustment.Result!.Adjustment!.Id), CancellationToken.None);
        Assert.Equal(EntryOperationStatus.Invalid, invalid.Status);
        var valid = await entries.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 50m, Today, null, Guid.NewGuid(), adjustment.Result.Adjustment.Id), CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Created, valid.Status);
        Assert.Equal(950m, (await cash.GetOverviewAsync(CancellationToken.None))!.Balance);
        Assert.Equal(0m, (await cash.GetOverviewAsync(CancellationToken.None))!.AdjustmentTotal);
        Assert.Single(await context.Db.LedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task UnconfirmedHolding_DoesNotInventHistoricalValueAndBlocksTradingOnly()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var opening = await funds.CreatePositionAsync(Position(Today.AddDays(-20), 10m, Today), CancellationToken.None);
        var fact = await context.Db.AssetTransactions.SingleAsync();
        context.Db.Entry(fact).Property(item => item.NeedsDateConfirmation).CurrentValue = true;
        await context.Db.SaveChangesAsync();
        var service = new HistoryService(context.Repository, context.Dates);
        var past = (await service.GetAsync(Today.AddDays(-10), CancellationToken.None))!;
        Assert.Null(past.Snapshot.TotalAssets);
        Assert.Null(past.Snapshot.Funds.Single().Value);
        Assert.False(past.Snapshot.Funds.Single().CanRebuild);
        Assert.Equal(EntryOperationStatus.Created, (await new LedgerEntryService(context.Repository, context.Dates)
            .CreateAsync(new(LedgerEntryKind.Income, "工资", 10m, Today, null, Guid.NewGuid()), CancellationToken.None)).Status);
        Assert.Equal(AssetOperationStatus.Invalid, (await funds.BuyAsync(new(opening.Result!.Position.Id, 10m, Today, null, Guid.NewGuid()), CancellationToken.None)).Status);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var request = Change(history, history.Facts.Single(item => item.Id == fact.Id));
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.True(preview.Result!.After.Funds.Single().CanRebuild);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None)).Status);
        Assert.False((await service.GetAsync(null, CancellationToken.None))!.Facts.Single(item => item.Id == fact.Id).NeedsDateConfirmation);
    }

    [Fact]
    public async Task ProvidentOpening_IsNotDepositOrIncome()
    {
        await using var context = await CreateAsync();
        var service = new ProvidentFundService(context.Repository, context.Dates);
        var result = await service.SetOpeningAsync(new(5000m, Today.AddDays(-10), null, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Created, result.Status);
        var overview = (await service.GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(5000m, overview.Balance);
        Assert.Equal(0m, overview.MonthDeposit);
        Assert.Equal(0m, overview.MonthWithdrawal);
        Assert.Equal(ProvidentActivityType.Opening, overview.Activities.Single().Type);
        Assert.Equal(10000m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task HistoricalSnapshot_UsesOnlyEffectiveQuotesAndNoFutureHolding()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var opening = await funds.CreatePositionAsync(Position(Today.AddDays(-10), 10m, Today.AddDays(-10)), CancellationToken.None);
        await funds.UpdateQuoteAsync(new(opening.Result!.Position.Id, 20m, Today, Guid.NewGuid()), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(Today.AddDays(-5), CancellationToken.None))!;
        Assert.Equal(1000m, history.Snapshot.Funds.Single().Value);
        Assert.Equal(Today.AddDays(-10), history.Snapshot.Funds.Single().PriceDate);
        Assert.Equal("manual", history.Snapshot.Funds.Single().PriceSource);
        Assert.Null((await service.GetAsync(Today.AddDays(-11), CancellationToken.None))!.Snapshot.Funds.Single().Value);
    }

    [Fact]
    public async Task RemovingOnlyQuote_DoesNotExposeSupersededValueInHome()
    {
        await using var context = await CreateAsync();
        await new FundService(context.Repository, context.Dates).CreatePositionAsync(Position(Today.AddDays(-10), 10m, Today), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var request = Change(history, history.Facts.Single()) with { Price = null, PriceDate = null };
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(new(request, preview.Result!.PreviewToken), CancellationToken.None)).Status);
        Assert.Null((await service.GetAsync(null, CancellationToken.None))!.Snapshot.Funds.Single().Value);
        Assert.Null((await new LedgerService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!.Fund.Amount);
    }

    [Fact]
    public async Task ManualSequence_AdvancesAutomaticAllocationWithoutDuplicateFacts()
    {
        await using var context = await CreateAsync(0m);
        var entries = new LedgerEntryService(context.Repository, context.Dates);
        await entries.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today, null, Guid.NewGuid()), CancellationToken.None);
        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var request = Change(history, history.Facts.Single()) with { Sequence = 2 };
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(new(request, preview.Result!.PreviewToken), CancellationToken.None)).Status);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(EntryOperationStatus.Created, (await entries.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 50m, Today, null, Guid.NewGuid()), CancellationToken.None)).Status);
        var facts = (await service.GetAsync(null, CancellationToken.None))!.Facts;
        Assert.Equal(facts.Count, facts.Select(fact => fact.Sequence).Distinct().Count());
        Assert.Equal(3, facts.Single(fact => fact.EntryKind == LedgerEntryKind.Expense).Sequence);
    }

    private static HistoryChangeRequest Change(HistoryOverviewDto history, BusinessFact fact)
    {
        return new(Guid.NewGuid(), history.LedgerVersion, fact.Source, fact.Id, fact.Version, "correct", fact.Date, fact.Sequence,
            fact.TargetBalance ?? fact.Amount, fact.Quantity, fact.PriceDate is null ? null : fact.Price, fact.PriceDate, fact.EntryKind, fact.Category, fact.Note, "测试更正", fact.RelatedCashAdjustmentId);
    }

    private static CreateFundPositionRequest Position(DateOnly holdingDate, decimal value, DateOnly valueDate)
    {
        return JsonSerializer.Deserialize<CreateFundPositionRequest>(JsonSerializer.Serialize(new
        {
            Name = "测试基金",
            Code = "000001",
            ShareClass = "A",
            Platform = "测试",
            Shares = 100m,
            Cost = 1000m,
            NetValue = value,
            NetValueDate = valueDate,
            HoldingDate = holdingDate,
            RequestId = Guid.NewGuid()
        }))!;
    }

    private static async Task<TestContext> CreateAsync(decimal balance = 10000m)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(new(Today.AddMonths(-2), balance), CancellationToken.None);
        return new(db, repository, dates);
    }

    private sealed record TestContext(AppDbContext Db, LedgerRepository Repository, BusinessDateProvider Dates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
