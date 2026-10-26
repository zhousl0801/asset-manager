using AssetKeeper.Tests.Quotes;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Provident;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Plans;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Persistence;

[Collection("PlanExecution")]
public sealed class G2PostgresTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly BusinessDateProvider Dates = new(new FixedTimeProvider(new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero)));

    [PostgresFact]
    public async Task PendingAndConfirmBuy_PersistFeeAndLinkAcrossContexts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid pendingId;

        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 10000m), CancellationToken.None);
            var funds = new FundService(repository, Dates);
            var position = await TestFundQuotes.CreatePositionAsync(repository, Dates,
                new("基金", "000001", null, null, 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)),
                CancellationToken.None);
            var pending = await funds.BuyAsync(
                new(position.Result!.Position.Id, 300m, Today, null, Guid.NewGuid(), Fee: 2m),
                CancellationToken.None);
            pendingId = pending.Result!.Activity.Id;
            var confirm = await funds.ConfirmBuyAsync(
                pendingId,
                new ConfirmFundBuyRequest(10m, 29.8m, Today, 2m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Created, confirm.Status);
        }

        await using var readback = database.CreateContext();
        var transactions = await readback.AssetTransactions.OrderBy(transaction => transaction.CreatedAt).ToListAsync();
        var pendingRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.FundBuyPending);
        var confirmRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.FundBuyConfirm);

        Assert.Equal(pendingId, pendingRow.Id);
        Assert.Equal(pendingId, confirmRow.LinkedTransactionId);
        Assert.Equal(2m, confirmRow.Fee);
        Assert.Equal(9700m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(110m, (await readback.FundPositions.SingleAsync()).Shares);
        Assert.Equal(1300m, (await readback.FundPositions.SingleAsync()).Cost);

        var overview = await new FundService(new LedgerRepository(readback), Dates).GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(3278m, overview.TotalValue);
        Assert.Equal(0m, overview.PendingAmount);
    }

    [PostgresFact]
    public async Task AutoPlanExecution_PersistsSingleOccurrenceAndEntry()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            var planRepository = new PlanRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 1000m), CancellationToken.None);
            var entries = new LedgerEntryService(repository, Dates);
            var provident = new ProvidentFundService(repository, Dates);
            var funds = new FundService(repository, Dates);
            var plans = new PlanService(repository, planRepository, Dates);
            var execution = new PlanExecutionService(repository, planRepository, Dates, entries, provident, funds);
            var save = await plans.SaveAsync(
                new SavePlanRequest(
                    null,
                    AutoPlanType.SalaryIncome,
                    "工资",
                    1000m,
                    AutoPlanCycle.Monthly,
                    Today.Day,
                    null,
                    new TimeOnly(9, 0),
                    Today,
                    null,
                    Guid.NewGuid(),
                    null),
                CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Created, save.Status);

            var first = await execution.RunDueAsync(CancellationToken.None);
            var second = await execution.RunDueAsync(CancellationToken.None);

            Assert.Equal(1, first.Completed);
            Assert.Equal(0, second.Completed);
        }

        await using var readback = database.CreateContext();
        Assert.Single(await readback.PlanOccurrences.ToListAsync());
        Assert.Equal(PlanOccurrenceStatus.Completed, (await readback.PlanOccurrences.SingleAsync()).Status);
        Assert.Single(await readback.LedgerEntries.ToListAsync());
        Assert.Equal(2000m, (await readback.CashAccounts.SingleAsync()).Balance);
    }

    [PostgresFact]
    public async Task QuoteRefresh_PersistsSourceAndSyncStatus()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid positionId;

        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 10000m), CancellationToken.None);
            var funds = new FundService(repository, Dates, new StubFundQuoteProvider());
            var position = await TestFundQuotes.CreatePositionAsync(repository, Dates,
                new("基金", "000001", null, null, 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)),
                CancellationToken.None);
            positionId = position.Result!.Position.Id;
            var refresh = await funds.RefreshQuoteAsync(positionId, CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Success, refresh.Status);
        }

        await using var readback = database.CreateContext();
        var positionRow = await readback.FundPositions.SingleAsync();
        var quoteRow = await readback.AssetTransactions.SingleAsync(transaction => transaction.Kind == AssetTransactionKind.FundQuote);

        Assert.Equal(positionId, positionRow.Id);
        Assert.Equal(1.25m, positionRow.NetValue);
        Assert.Equal("eastmoney", positionRow.LastQuoteSource);
        Assert.Null(positionRow.LastQuoteError);
        Assert.NotNull(positionRow.LastQuoteAttemptAt);
        Assert.Equal("eastmoney", quoteRow.QuoteSource);
        Assert.Contains(await readback.PriceObservations.ToListAsync(), price => price.Source == "eastmoney");
    }

    [PostgresFact]
    public async Task GoldSellPendingAndArrival_PersistLinkAndCash()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid pendingId;

        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 10000m), CancellationToken.None);
            var gold = new GoldService(repository, Dates);
            await gold.SaveHoldingAsync(
                new SaveGoldHoldingRequest(30m, 25600m, 900m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)),
                CancellationToken.None);
            var pending = await gold.SellAsync(
                new SellGoldRequest(1m, 898m, Today, null, Guid.NewGuid(), Settlement: GoldSellSettlement.PendingArrival),
                CancellationToken.None);
            pendingId = pending.Result!.Activity.Id;
            var arrival = await gold.ArriveSellAsync(
                pendingId,
                new GoldSellArrivalRequest(898m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Created, arrival.Status);
        }

        await using var readback = database.CreateContext();
        var transactions = await readback.AssetTransactions.OrderBy(transaction => transaction.CreatedAt).ToListAsync();
        var pendingRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.GoldSellPending);
        var arrivalRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.GoldSellArrival);

        Assert.Equal(pendingId, pendingRow.Id);
        Assert.Equal(pendingId, arrivalRow.LinkedTransactionId);
        Assert.Equal(10898m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(29m, (await readback.GoldHoldings.SingleAsync()).Grams);
        Assert.Equal(24746.67m, (await readback.GoldHoldings.SingleAsync()).Cost);

        var overview = await new GoldService(new LedgerRepository(readback), Dates).GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(26100m, overview.Value);
        Assert.Equal(0m, overview.PendingArrivalAmount);
        Assert.Empty(overview.PendingArrivals!);
    }

    [PostgresFact]
    public async Task SellApplyConfirmAndArrival_PersistLinksAndCash()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid applyId;
        Guid confirmId;

        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 10000m), CancellationToken.None);
            var funds = new FundService(repository, Dates);
            var position = await TestFundQuotes.CreatePositionAsync(repository, Dates,
                new("基金", null, null, null, 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)),
                CancellationToken.None);
            var apply = await funds.ApplySellAsync(
                new(position.Result!.Position.Id, 10m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            applyId = apply.Result!.Activity.Id;
            var confirm = await funds.ConfirmSellAsync(
                applyId,
                new ConfirmFundSellRequest(10m, 10m, Today, 1m, 100m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            confirmId = confirm.Result!.Activity.Id;
            var arrival = await funds.ArriveSellAsync(
                confirmId,
                new FundSellArrivalRequest(100m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Created, arrival.Status);
        }

        await using var readback = database.CreateContext();
        var transactions = await readback.AssetTransactions.OrderBy(transaction => transaction.CreatedAt).ToListAsync();
        var applyRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.FundSellApply);
        var confirmRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.FundSellConfirm);
        var arrivalRow = transactions.Single(transaction => transaction.Kind == AssetTransactionKind.FundSellArrival);

        Assert.Equal(applyId, applyRow.Id);
        Assert.Equal(applyId, confirmRow.LinkedTransactionId);
        Assert.Equal(confirmId, arrivalRow.LinkedTransactionId);
        Assert.Equal(1m, confirmRow.Fee);
        Assert.Equal(10100m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(90m, (await readback.FundPositions.SingleAsync()).Shares);
        Assert.Equal(900m, (await readback.FundPositions.SingleAsync()).Cost);

        var overview = await new FundService(new LedgerRepository(readback), Dates).GetOverviewAsync(CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(900m, overview.TotalValue);
        Assert.Equal(0m, overview.PendingArrivalAmount);
        Assert.Empty(overview.PendingArrivals!);
    }

    private sealed class StubFundQuoteProvider : IFundQuoteProvider
    {
        public Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExternalFundQuote(code, 1.25m, Today, "eastmoney", DateTimeOffset.UtcNow));
        }
    }
}
