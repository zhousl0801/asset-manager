using System.Text.Json;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.History;

public sealed class G2ServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReversePendingBuy_RestoresCashAndRemovesPending()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await BuyPendingAsync(funds, positionId, 300m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundBuyPending);
        var request = Change(history, fact) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(10000m, preview.Result!.After.CashBalance);
        Assert.Equal(0m, preview.Result.After.Funds.Single().PendingAmount);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(10000m, overview.CashBalance);
        Assert.Equal(0m, overview.PendingAmount);
        Assert.Equal(100m, overview.Positions.Single().Shares);
    }

    [Fact]
    public async Task ReverseConfirmedBuy_ReturnsToPendingWithoutChangingCash()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await BuyAndConfirmAsync(funds, positionId, 300m, 10m, 30m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var confirm = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundBuyConfirm);
        var request = Change(history, confirm) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(9700m, preview.Result!.After.CashBalance);
        Assert.Equal(300m, preview.Result.After.Funds.Single().PendingAmount);
        Assert.Equal(100m, preview.Result.After.Funds.Single().Quantity);
        Assert.Equal(1000m, preview.Result.After.Funds.Single().Cost);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(9700m, overview.CashBalance);
        Assert.Equal(300m, overview.PendingAmount);
        Assert.Equal(100m, overview.Positions.Single().Shares);
        Assert.Equal(1000m, overview.Positions.Single().Cost);
    }

    [Fact]
    public async Task ReversePendingBuy_AfterConfirmation_IsRejected()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await BuyAndConfirmAsync(funds, positionId, 300m, 10m, 30m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var pending = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundBuyPending);
        var request = Change(history, pending) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("已确认"));
    }

    [Fact]
    public async Task CorrectPendingAmount_AfterConfirmation_IsRejected()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await BuyAndConfirmAsync(funds, positionId, 300m, 10m, 30m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var pending = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundBuyPending);
        var request = Change(history, pending) with { Amount = 250m };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("已确认"));
    }

    [Fact]
    public async Task CorrectConfirmedAmount_IsRejectedWithLinkExplanation()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await BuyAndConfirmAsync(funds, positionId, 300m, 10m, 30m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var confirm = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundBuyConfirm);
        var request = Change(history, confirm) with { Amount = 299m };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("待确认记录决定"));
    }

    [Fact]
    public async Task CorrectPendingAmount_BeforeConfirmation_UpdatesCashAndPending()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var pendingActivity = await BuyPendingAsync(funds, positionId, 300m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var pending = history.Facts.Single(item => item.Id == pendingActivity.Id);
        var request = Change(history, pending) with { Amount = 250m };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(9750m, preview.Result!.After.CashBalance);
        Assert.Equal(250m, preview.Result.After.Funds.Single().PendingAmount);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(9750m, overview.CashBalance);
        Assert.Equal(250m, overview.PendingAmount);
        Assert.Equal(250m, overview.PendingBuys!.Single().Amount);
    }

    [Fact]
    public async Task ReverseSellApply_UnfreezesSharesWithoutChangingCash()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        await ApplySellAsync(funds, positionId, 10m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellApply);
        var request = Change(history, fact) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(10000m, preview.Result!.After.CashBalance);
        Assert.Equal(0m, preview.Result.After.Funds.Single().AppliedShares);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(10000m, overview.CashBalance);
        Assert.Empty(overview.SellApplications!);
        Assert.Equal(100m, overview.Positions.Single().Shares);
    }

    [Fact]
    public async Task ReverseSellConfirm_ReturnsSharesAndRemovesPendingArrival()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var apply = await ApplySellAsync(funds, positionId, 10m);
        await ConfirmSellAsync(funds, apply.Id, 10m, 10m, 100m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var confirm = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellConfirm);
        var request = Change(history, confirm) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(100m, preview.Result!.After.Funds.Single().Quantity);
        Assert.Equal(1000m, preview.Result.After.Funds.Single().Cost);
        Assert.Equal(0m, preview.Result.After.Funds.Single().PendingArrivalAmount);
        Assert.Equal(10m, preview.Result.After.Funds.Single().AppliedShares);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(10000m, overview.CashBalance);
        Assert.Single(overview.SellApplications!);
        Assert.Empty(overview.PendingArrivals!);
        Assert.Equal(100m, overview.Positions.Single().Shares);
    }

    [Fact]
    public async Task ReverseSellConfirm_AfterArrival_IsRejected()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var apply = await ApplySellAsync(funds, positionId, 10m);
        var confirm = await ConfirmSellAsync(funds, apply.Id, 10m, 10m, 100m);
        await ArriveSellAsync(funds, confirm.Id, 100m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellConfirm);
        var preview = await service.PreviewAsync(Change(history, fact) with { Action = "reverse" }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("已到账"));
    }

    [Fact]
    public async Task ReverseSellArrival_RestoresPendingArrivalAndRemovesCash()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var apply = await ApplySellAsync(funds, positionId, 10m);
        var confirm = await ConfirmSellAsync(funds, apply.Id, 10m, 10m, 100m);
        await ArriveSellAsync(funds, confirm.Id, 100m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var arrival = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellArrival);
        var request = Change(history, arrival) with { Action = "reverse" };
        var preview = await service.PreviewAsync(request, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(10000m, preview.Result!.After.CashBalance);
        Assert.Equal(100m, preview.Result.After.Funds.Single().PendingArrivalAmount);

        var result = await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Success, result.Status);
        context.Db.ChangeTracker.Clear();
        var overview = (await new FundService(context.Repository, context.Dates).GetOverviewAsync(CancellationToken.None))!;
        Assert.Equal(10000m, overview.CashBalance);
        Assert.Single(overview.PendingArrivals!);
        Assert.Equal(100m, overview.PendingArrivalAmount);
    }

    [Fact]
    public async Task CorrectSellArrivalAmount_IsRejected()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var apply = await ApplySellAsync(funds, positionId, 10m);
        var confirm = await ConfirmSellAsync(funds, apply.Id, 10m, 10m, 100m);
        await ArriveSellAsync(funds, confirm.Id, 100m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var arrival = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellArrival);
        var preview = await service.PreviewAsync(Change(history, arrival) with { Amount = 99m }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("必须与待到账金额一致"));
    }

    [Fact]
    public async Task CorrectSellApply_AfterConfirmation_IsRejected()
    {
        await using var context = await CreateAsync();
        var funds = new FundService(context.Repository, context.Dates);
        var positionId = await CreatePositionAsync(funds);
        var apply = await ApplySellAsync(funds, positionId, 10m);
        await ConfirmSellAsync(funds, apply.Id, 10m, 10m, 100m);

        var service = new HistoryService(context.Repository, context.Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundSellApply);
        var preview = await service.PreviewAsync(Change(history, fact) with { Quantity = 5m }, CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Invalid, preview.Status);
        Assert.Contains(preview.Errors, error => error.Contains("已确认"));
    }

    private static async Task<FundActivityDto> ApplySellAsync(FundService funds, Guid positionId, decimal shares)
    {
        var result = await funds.ApplySellAsync(new SellFundRequest(positionId, shares, Today, null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Activity;
    }

    private static async Task<FundActivityDto> ConfirmSellAsync(
        FundService funds,
        Guid applyId,
        decimal shares,
        decimal netValue,
        decimal netAmount)
    {
        var result = await funds.ConfirmSellAsync(
            applyId,
            new ConfirmFundSellRequest(shares, netValue, Today, 0m, netAmount, Today, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Activity;
    }

    private static async Task<FundActivityDto> ArriveSellAsync(FundService funds, Guid confirmId, decimal amount)
    {
        var result = await funds.ArriveSellAsync(
            confirmId,
            new FundSellArrivalRequest(amount, Today, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Activity;
    }

    private static async Task<Guid> CreatePositionAsync(FundService funds)
    {
        var result = await funds.CreatePositionAsync(Position(Today.AddDays(-20), 10m, Today.AddDays(-20)), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Position.Id;
    }

    private static async Task<FundActivityDto> BuyPendingAsync(FundService funds, Guid positionId, decimal amount)
    {
        var result = await funds.BuyAsync(new BuyFundRequest(positionId, amount, Today, null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);

        return result.Result!.Activity;
    }

    private static async Task BuyAndConfirmAsync(FundService funds, Guid positionId, decimal amount, decimal shares, decimal netValue)
    {
        var pending = await BuyPendingAsync(funds, positionId, amount);
        var result = await funds.ConfirmBuyAsync(
            pending.Id,
            new ConfirmFundBuyRequest(shares, netValue, Today, 0m, Today, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AssetOperationStatus.Created, result.Status);
    }

    private static HistoryChangeRequest Change(HistoryOverviewDto history, BusinessFact fact)
    {
        return new(Guid.NewGuid(), history.LedgerVersion, fact.Source, fact.Id, fact.Version, "correct", fact.Date, fact.Sequence,
            fact.TargetBalance ?? fact.Amount, fact.Quantity, fact.PriceDate is null ? null : fact.Price, fact.PriceDate,
            fact.EntryKind, fact.Category, fact.Note, "测试更正", fact.RelatedCashAdjustmentId, fact.Fee);
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
