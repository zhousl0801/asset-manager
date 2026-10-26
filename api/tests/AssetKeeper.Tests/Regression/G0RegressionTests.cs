using AssetKeeper.Tests.Quotes;
using System.Text.Json;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Regression;

public sealed class G0RegressionTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OlderQuote_DoesNotReplaceLatestValue()
    {
        var position = FundPosition.Create(Guid.NewGuid(), "基金", null, null, null, 100m, 1000m, 15m, Today, Now);
        position.UpdateQuote(10m, Today.AddDays(-1), Now);

        Assert.Equal(1500m, position.Value);
        Assert.Equal(Today, position.NetValueDate);
        var gold = GoldHolding.Create(Guid.NewGuid(), 10m, 1000m, 150m, Today, Now);
        gold.Buy(1m, 100m, 100m, Today.AddDays(-1), Now);
        Assert.Equal(150m, gold.Quote);
    }

    [Fact]
    public async Task SameRequestWithDifferentAmount_ReturnsConflict()
    {
        await using var context = await CreateContextAsync();
        var service = new ProvidentFundService(context.Repository, context.Dates);
        var request = new ProvidentDepositRequest(100m, Today, null, Guid.NewGuid());
        await service.DepositAsync(request, CancellationToken.None);

        var result = await service.DepositAsync(request with { Amount = 200m }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Conflict, result.Status);
        Assert.Equal(100m, (await service.GetOverviewAsync(CancellationToken.None))!.Balance);
    }

    [Fact]
    public async Task HistoricalBuyBeforeSell_ReplaysCostAndRealizedProfit()
    {
        await using var context = await CreateContextAsync(3000m);
        var service = new FundService(context.Repository, context.Dates);
        var opening = await TestFundQuotes.CreatePositionAsync(context.Repository, context.Dates, new("基金", "000001", null, null, 100m, 1000m, 10m, Today.AddDays(-20), null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)), CancellationToken.None);
        var id = opening.Result!.Position.Id;
        var sellApply = await service.ApplySellAsync(new(id, 50m, Today.AddDays(-6), null, Guid.NewGuid()), CancellationToken.None);
        await service.ConfirmSellAsync(sellApply.Result!.Activity.Id,
            new ConfirmFundSellRequest(50m, 20m, Today.AddDays(-6), 0m, 1000m, Today.AddDays(-6), null, Guid.NewGuid()), CancellationToken.None);

        var pending = await service.BuyAsync(new(id, 2000m, Today.AddDays(-11), null, Guid.NewGuid()), CancellationToken.None);
        var result = await service.ConfirmBuyAsync(pending.Result!.Activity.Id,
            new ConfirmFundBuyRequest(100m, 20m, Today.AddDays(-11), 0m, Today.AddDays(-11), null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
        var position = (await service.GetOverviewAsync(CancellationToken.None))!.Positions.Single();
        Assert.Equal(2250m, position.Cost);
        Assert.Equal(250m, position.RealizedProfit);
    }

    [Fact]
    public async Task CashReconcileRetry_DoesNotOverrideLaterIncome()
    {
        await using var context = await CreateContextAsync();
        var service = new CashService(context.Repository, context.Dates);
        var request = ReconcileRequest(900m, 0);
        await service.ReconcileAsync(request, CancellationToken.None);
        await new LedgerEntryService(context.Repository, context.Dates).CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var result = await service.ReconcileAsync(request, CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Success, result.Status);
        Assert.Equal(1000m, (await service.GetOverviewAsync(CancellationToken.None))!.Balance);
        Assert.Single(await context.Db.CashAdjustments.ToListAsync());
    }

    [Fact]
    public async Task CashReconcileWithStalePage_ReturnsConflict()
    {
        await using var context = await CreateContextAsync();
        await new LedgerEntryService(context.Repository, context.Dates).CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var result = await new CashService(context.Repository, context.Dates).ReconcileAsync(ReconcileRequest(900m, 0), CancellationToken.None);

        Assert.Equal(CashReconcileStatus.Conflict, result.Status);
        Assert.Equal(1100m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task AnnualSummary_IncludesMoreThanFiveThousandEntries()
    {
        await using var context = await CreateContextAsync();
        var ledger = (await context.Repository.GetAsync(CancellationToken.None))!;
        context.Db.LedgerEntries.AddRange(Enumerable.Range(0, 5001).Select(_ => LedgerEntry.Create(ledger.Id, LedgerEntryKind.Income, "工资", 1m, Today, ledger.StartDate, Today, null, LedgerEntrySource.Manual, Guid.NewGuid(), Now)));
        await context.Db.SaveChangesAsync();

        var result = await new LedgerEntryService(context.Repository, context.Dates).GetPeriodAsync(EntryPeriod.Year, Today, null, null, null, CancellationToken.None);

        Assert.Equal(5001m, result!.Income);
    }

    [Fact]
    public async Task ProvidentPeriod_IsIndependentOfBalanceDate()
    {
        await using var context = await CreateContextAsync();
        var service = new ProvidentFundService(context.Repository, context.Dates);
        await service.DepositAsync(new(100m, Today.AddMonths(-1), null, Guid.NewGuid()), CancellationToken.None);
        var overview = await service.GetOverviewAsync(CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(overview);

        Assert.True(json.TryGetProperty("PeriodFrom", out var from));
        Assert.Equal("2026-10-01", from.GetString());
        Assert.Equal(Today.AddMonths(-1), overview!.AsOfDate);
    }

    [Fact]
    public async Task ChangingOnlyDate_AlsoAdvancesCashVersion()
    {
        await using var context = await CreateContextAsync();
        var entries = new LedgerEntryService(context.Repository, context.Dates);
        var created = await entries.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today.AddDays(-2), null, Guid.NewGuid()), CancellationToken.None);
        var version = (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Version;

        var result = await entries.UpdateAsync(created.Result!.Entry.Id,
            new(LedgerEntryKind.Income, "工资", 100m, Today, null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Success, result.Status);
        Assert.Equal(version + 1, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Version);
    }

    [Fact]
    public async Task ProvidentWithdrawalBeforeCashReconciliation_IsRejected()
    {
        await using var context = await CreateContextAsync();
        var provident = new ProvidentFundService(context.Repository, context.Dates);
        await provident.DepositAsync(new(500m, Today.AddDays(-20), null, Guid.NewGuid()), CancellationToken.None);
        await new CashService(context.Repository, context.Dates).ReconcileAsync(ReconcileRequest(500m, 0), CancellationToken.None);

        var result = await provident.WithdrawAsync(new(100m, Today.AddDays(-10), null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        Assert.Equal(500m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
        Assert.Equal(500m, (await provident.GetOverviewAsync(CancellationToken.None))!.Balance);
    }

    [Fact]
    public async Task SameIdAcrossDifferentOperations_ReturnsConflict()
    {
        await using var context = await CreateContextAsync();
        var service = new ProvidentFundService(context.Repository, context.Dates);
        var id = Guid.NewGuid();
        await service.DepositAsync(new(100m, Today, null, id), CancellationToken.None);

        var result = await service.WithdrawAsync(new(100m, Today, null, id), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Conflict, result.Status);
        Assert.Equal(1000m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
        Assert.Single(await context.Db.MutationReceipts.ToListAsync());
    }

    [Fact]
    public async Task EquivalentDecimalRepresentation_ReplaysOriginalReceipt()
    {
        await using var context = await CreateContextAsync();
        var service = new ProvidentFundService(context.Repository, context.Dates);
        var request = new ProvidentDepositRequest(100m, Today, " note ", Guid.NewGuid());
        var first = await service.DepositAsync(request, CancellationToken.None);
        var replay = await service.DepositAsync(request with { Amount = 100.00m, Note = "note" }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, replay.Status);
        Assert.Equal(first.Result!.Activity.Id, replay.Result!.Activity.Id);
        Assert.Equal(100m, replay.Result.Balance);
    }

    [Fact]
    public async Task CashCannotSpendLaterIncomeAtAnEarlierDate()
    {
        await using var context = await CreateContextAsync();
        var service = new LedgerEntryService(context.Repository, context.Dates);
        await service.CreateAsync(new(LedgerEntryKind.Income, "工资", 1000m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var result = await service.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 1500m, Today.AddDays(-1), null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EntryOperationStatus.Invalid, result.Status);
        Assert.Equal(2000m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task GoldReconciliationRejectsOldVersionWithoutOverwritingBuy()
    {
        await using var context = await CreateContextAsync();
        var service = new GoldService(context.Repository, context.Dates);
        await service.SaveHoldingAsync(new(10m, 100m, 10m, Today, null, Guid.NewGuid(), 0, HoldingDate: Today), CancellationToken.None);
        await service.BuyAsync(new(1m, 10m, 10m, Today, Today, null, Guid.NewGuid()), CancellationToken.None);

        var result = await service.SaveHoldingAsync(new(10m, 100m, 10m, Today, null, Guid.NewGuid(), 0, HoldingDate: Today), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Conflict, result.Status);
        Assert.Equal(11m, (await service.GetOverviewAsync(CancellationToken.None))!.Grams);
        Assert.Equal(990m, (await context.Repository.GetAsync(CancellationToken.None))!.CashAccount.Balance);
    }

    [Fact]
    public async Task EntryCorrection_PersistsBeforeStateWithTheResult()
    {
        await using var context = await CreateContextAsync();
        var service = new LedgerEntryService(context.Repository, context.Dates);
        var created = await service.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today, "原备注", Guid.NewGuid()), CancellationToken.None);
        var id = Guid.NewGuid();
        await service.UpdateAsync(created.Result!.Entry.Id, new(LedgerEntryKind.Income, "工资", 200m, Today, "新备注", id), CancellationToken.None);
        var receipt = await context.Repository.FindMutationReceiptAsync((await context.Repository.GetAsync(CancellationToken.None))!.Id, id, CancellationToken.None);

        Assert.NotNull(receipt);
        using var before = JsonDocument.Parse(receipt.BeforeJson!);
        Assert.Equal(100m, before.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("原备注", before.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task PagedEntries_DoNotOmitOrDuplicateEqualDateAndCreationTimes()
    {
        await using var context = await CreateContextAsync();
        var ledger = (await context.Repository.GetAsync(CancellationToken.None))!;
        context.Db.LedgerEntries.AddRange(Enumerable.Range(0, 501).Select(_ => LedgerEntry.Create(ledger.Id, LedgerEntryKind.Income, "工资", 1m, Today, ledger.StartDate, Today, null, LedgerEntrySource.Manual, Guid.NewGuid(), Now)));
        await context.Db.SaveChangesAsync();
        var service = new LedgerEntryService(context.Repository, context.Dates);
        var ids = new List<Guid>();

        for (var page = 1; page <= 6; page++)
        {
            var result = await service.GetPeriodAsync(EntryPeriod.Month, Today, null, null, null, CancellationToken.None, page, 100);
            Assert.Equal(501m, result!.Income);
            Assert.Equal(501, result.TotalCount);
            ids.AddRange(result.Entries.Select(entry => entry.Id));
        }

        Assert.Equal(501, ids.Count);
        Assert.Equal(501, ids.Distinct().Count());
    }

    private static ReconcileCashRequest ReconcileRequest(decimal balance, int version)
    {
        return JsonSerializer.Deserialize<ReconcileCashRequest>(JsonSerializer.Serialize(new
        {
            ActualBalance = balance,
            Reason = "核对",
            RequestId = Guid.NewGuid(),
            ExpectedVersion = version
        }))!;
    }

    private static async Task<TestContext> CreateContextAsync(decimal balance = 1000m)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(new(Today.AddMonths(-2), balance), CancellationToken.None);
        return new TestContext(db, repository, dates);
    }

    private sealed record TestContext(AppDbContext Db, LedgerRepository Repository, BusinessDateProvider Dates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
