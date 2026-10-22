using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AssetKeeper.Tests.Persistence;

public sealed class G1PostgresTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly BusinessDateProvider Dates = new(new FixedTimeProvider(new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero)));

    [PostgresFact]
    public async Task Migration_PreservesCurrentHoldingsAndRequiresIndependentDateConfirmation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            var repository = new LedgerRepository(seed);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 1000m), CancellationToken.None);
            var funds = new FundService(repository, Dates);
            await funds.CreatePositionAsync(new("旧持仓", null, null, null, 100m, 1000m, 12m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
            await seed.GetService<IMigrator>().MigrateAsync("20261021075435_CloseG0Correctness");
        }

        await using (var upgrade = database.CreateContext())
        {
            await upgrade.Database.MigrateAsync();
            var position = await upgrade.FundPositions.SingleAsync();
            Assert.Equal(100m, position.Shares);
            Assert.Equal(1000m, position.Cost);
            Assert.Equal(1200m, position.Value);
            Assert.True((await upgrade.AssetTransactions.SingleAsync()).NeedsDateConfirmation);
            Assert.Equal(1000m, (await upgrade.CashAccounts.SingleAsync()).OpeningBalance);
            Assert.Contains(await upgrade.PriceObservations.ToListAsync(), price => price.Source == "migration:last-known");
            var repository = new LedgerRepository(upgrade);
            var history = (await new HistoryService(repository, Dates).GetAsync(Today.AddDays(-1), CancellationToken.None))!;
            Assert.Null(history.Snapshot.TotalAssets);
            Assert.Null(history.Snapshot.Funds.Single().Value);
            var cash = await new LedgerEntryService(repository, Dates).CreateAsync(new(LedgerEntryKind.Income, "工资", 10m, Today, null, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal(EntryOperationStatus.Created, cash.Status);
            Assert.Equal(100m, (await upgrade.FundPositions.SingleAsync()).Shares);
            var buy = await new FundService(repository, Dates).BuyAsync(new(position.Id, 1m, 12m, 12m, Today, Today, null, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal(AssetOperationStatus.Invalid, buy.Status);
        }
    }

    [PostgresFact]
    public async Task MigratedLatestQuote_CanBeCorrectedThroughItsOriginalFact()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            var repository = new LedgerRepository(seed);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 1000m), CancellationToken.None);
            await new FundService(repository, Dates).CreatePositionAsync(new("旧持仓", null, null, null, 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
            await seed.GetService<IMigrator>().MigrateAsync("20261021075435_CloseG0Correctness");
        }

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var service = new HistoryService(new LedgerRepository(context), Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var fact = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundOpening);
        var request = new HistoryChangeRequest(Guid.NewGuid(), history.LedgerVersion, fact.Source, fact.Id, fact.Version, "correct",
            Today, fact.Sequence, fact.Amount, fact.Quantity, 20m, Today.AddDays(-1), null, null, fact.Note, "更正旧净值日期与金额");
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, preview.Status);
        Assert.Equal(2000m, preview.Result!.After.Funds.Single().Value);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None)).Status);
        Assert.Equal(2000m, (await new FundService(new LedgerRepository(context), Dates).GetOverviewAsync(CancellationToken.None))!.TotalValue);
        Assert.DoesNotContain(await context.PriceObservations.ToListAsync(), quote => quote.SourceTransactionId is null);
    }

    [PostgresFact]
    public async Task MigratedUnmatchedSnapshot_HasAnEditableQuoteFact()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            var repository = new LedgerRepository(seed);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 1000m), CancellationToken.None);
            await new FundService(repository, Dates).CreatePositionAsync(new("旧持仓", null, null, null, 100m, 1000m, 10m, Today.AddDays(-1), null, Guid.NewGuid(), HoldingDate: Today.AddDays(-1)), CancellationToken.None);
            // A legacy cached quote can come from a buy whose independent quote date was not retained.
            (await seed.FundPositions.SingleAsync()).UpdateQuote(12m, Today, DateTimeOffset.UtcNow);
            await seed.SaveChangesAsync();
            await seed.GetService<IMigrator>().MigrateAsync("20261021075435_CloseG0Correctness");
        }

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var service = new HistoryService(new LedgerRepository(context), Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var quote = history.Facts.Single(item => item.Kind == AssetTransactionKind.FundQuote);
        Assert.False(quote.NeedsDateConfirmation);
        Assert.Equal(Today, quote.PriceDate);
        Assert.Contains("迁移", quote.Note);
        var request = new HistoryChangeRequest(Guid.NewGuid(), history.LedgerVersion, quote.Source, quote.Id, quote.Version, "correct",
            Today, quote.Sequence, 0m, null, 15m, Today, null, null, quote.Note, "更正迁移行情");
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(1500m, preview.Result!.After.Funds.Single().Value);
        Assert.Equal(AssetOperationStatus.Success, (await service.ChangeAsync(new(request, preview.Result.PreviewToken), CancellationToken.None)).Status);
        Assert.DoesNotContain(await context.PriceObservations.ToListAsync(), price => price.SourceTransactionId is null);
    }

    [PostgresFact]
    public async Task InvalidReplay_DoesNotPersistFactQuoteReceiptOrProjectedBalance()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new LedgerRepository(context);
        await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 100m), CancellationToken.None);
        var funds = new FundService(repository, Dates);
        var opening = await funds.CreatePositionAsync(new("基金", null, null, null, 10m, 100m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today.AddDays(-20)), CancellationToken.None);
        var result = await funds.BuyAsync(new(opening.Result!.Position.Id, 20m, 200m, 10m, Today, Today.AddDays(-1), null, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Invalid, result.Status);
        await using var readback = database.CreateContext();
        Assert.Equal(100m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(10m, (await readback.FundPositions.SingleAsync()).Shares);
        Assert.Equal(1, await readback.AssetTransactions.CountAsync());
        Assert.Equal(1, await readback.PriceObservations.CountAsync());
        Assert.Equal(1, await readback.MutationReceipts.CountAsync());
    }

    [PostgresFact]
    public async Task LedgerVersion_ProtectsPreviewAcrossIndependentAssetWrites()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new LedgerRepository(context);
        await new LedgerService(repository, Dates).InitializeAsync(new(Today, 1000m), CancellationToken.None);
        var service = new HistoryService(repository, Dates);
        var history = (await service.GetAsync(null, CancellationToken.None))!;
        var opening = history.CashOpening;
        var request = new HistoryChangeRequest(Guid.NewGuid(), history.LedgerVersion, "cashOpening", opening.Id, opening.Version,
            "correct", Today, 0, 2000m, null, null, null, null, null, null, "起始余额录错");
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        await using (var other = database.CreateContext())
        {
            await new ProvidentFundService(new LedgerRepository(other), Dates).DepositAsync(new(100m, Today, null, Guid.NewGuid()), CancellationToken.None);
        }

        // Simulate a still-tracked old page: the shared CAS must roll everything back.
        var result = await service.ChangeAsync(new(request, preview.Result!.PreviewToken), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Conflict, result.Status);
        await using var readback = database.CreateContext();
        Assert.Equal(1000m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(100m, (await readback.ProvidentFundAccounts.SingleAsync()).Balance);
        Assert.Equal(1, await readback.MutationReceipts.CountAsync());
    }
}
