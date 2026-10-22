using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.History;

public sealed class G2GoldSellTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReverseGoldSellPending_RestoresGramsAndRemovesPendingArrival()
    {
        await using var context = await CreateAsync();
        var gold = new GoldService(context.Repository, context.Dates);
        await SaveHoldingAsync(gold);
        await SellPendingAsync(gold, 1m, 898m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.GoldSellPending);
        var request = Change(history, fact) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(10000m, preview.Result!.After.CashBalance);
        Assert.Equal(30m, preview.Result.After.Gold!.Quantity);
        Assert.Equal(0m, preview.Result.After.Gold.PendingArrivalAmount);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new GoldService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(30m, overview.Grams);
        Assert.Equal(0m, overview.PendingArrivalAmount);
    }

    [Fact]
    public async Task ReverseGoldSellArrival_RestoresPendingArrivalAndRemovesCash()
    {
        await using var context = await CreateAsync();
        var gold = new GoldService(context.Repository, context.Dates);
        await SaveHoldingAsync(gold);
        var pending = await SellPendingAsync(gold, 1m, 898m);
        await gold.ArriveSellAsync(pending, new GoldSellArrivalRequest(898m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var arrival = history.Facts.Single(item => item.Kind == AssetTransactionKind.GoldSellArrival);
        var request = Change(history, arrival) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(10000m, preview.Result!.After.CashBalance);
        Assert.Equal(898m, preview.Result.After.Gold!.PendingArrivalAmount);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new GoldService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(10000m, overview.CashBalance);
        Assert.Equal(898m, overview.PendingArrivalAmount);
    }

    [Fact]
    public async Task ReverseGoldSellPending_AfterArrival_IsRejected()
    {
        await using var context = await CreateAsync();
        var gold = new GoldService(context.Repository, context.Dates);
        await SaveHoldingAsync(gold);
        var pending = await SellPendingAsync(gold, 1m, 898m);
        await gold.ArriveSellAsync(pending, new GoldSellArrivalRequest(898m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.GoldSellPending);
        var preview = await service.PreviewAsync(Change(history, fact) with { Action = "reverse" }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("已到账"));
    }

    [Fact]
    public async Task CorrectGoldSellArrivalAmount_IsRejected()
    {
        await using var context = await CreateAsync();
        var gold = new GoldService(context.Repository, context.Dates);
        await SaveHoldingAsync(gold);
        var pending = await SellPendingAsync(gold, 1m, 898m);
        await gold.ArriveSellAsync(pending, new GoldSellArrivalRequest(898m, Today, null, Guid.NewGuid()), CancellationToken.None);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var arrival = history.Facts.Single(item => item.Kind == AssetTransactionKind.GoldSellArrival);
        var preview = await service.PreviewAsync(Change(history, arrival) with { Amount = 897m }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("必须与待到账金额一致"));
    }

    private static async Task SaveHoldingAsync(GoldService gold)
    {
        var result = await gold.SaveHoldingAsync(
            new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
    }

    private static async Task<Guid> SellPendingAsync(GoldService gold, decimal grams, decimal netAmount)
    {
        var result = await gold.SellAsync(
            new SellGoldRequest(grams, netAmount, Today, null, Guid.NewGuid(), Settlement: GoldSellSettlement.PendingArrival),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Activity.Id;
    }

    private static HistoryChangeRequest Change(HistoryOverviewDto history, BusinessFact fact)
    {
        return new(Guid.NewGuid(), history.LedgerVersion, fact.Source, fact.Id, fact.Version, "correct", fact.Date, fact.Sequence,
            fact.TargetBalance ?? fact.Amount, fact.Quantity, fact.PriceDate is null ? null : fact.Price, fact.PriceDate,
            fact.EntryKind, fact.Category, fact.Note, "测试更正", fact.RelatedCashAdjustmentId, fact.Fee);
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
