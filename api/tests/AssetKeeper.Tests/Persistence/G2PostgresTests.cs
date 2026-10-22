using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Persistence;

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
            var position = await funds.CreatePositionAsync(
                new("基金", null, null, null, 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)),
                CancellationToken.None);
            var pending = await funds.BuyAsync(
                new(position.Result!.Position.Id, 300m, Today, null, Guid.NewGuid()),
                CancellationToken.None);
            pendingId = pending.Result!.Activity.Id;
            var confirm = await funds.ConfirmBuyAsync(
                pendingId,
                new ConfirmFundBuyRequest(10m, 30m, Today, 2m, Today, null, Guid.NewGuid()),
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
        Assert.Equal(3300m, overview.TotalValue);
        Assert.Equal(0m, overview.PendingAmount);
    }
}
