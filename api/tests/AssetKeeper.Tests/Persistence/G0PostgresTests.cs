using System.Data.Common;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AssetKeeper.Tests.Persistence;

public sealed class G0PostgresTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly BusinessDateProvider Dates = new(new FixedTimeProvider(Now));

    [PostgresFact]
    public async Task SimultaneousInitialization_OnlyOneLedgerAndCashAccountAreCreated()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var barrier = new InitializeBarrier();
        await using var first = database.CreateContext(barrier);
        await using var second = database.CreateContext(barrier);
        var request = new InitializeLedgerRequest(Today, 1000m);

        var results = await Task.WhenAll(
            new LedgerService(new LedgerRepository(first), Dates).InitializeAsync(request, CancellationToken.None),
            new LedgerService(new LedgerRepository(second), Dates).InitializeAsync(request, CancellationToken.None));

        Assert.Single(results, result => result.Status == InitializeLedgerStatus.Created);
        Assert.Single(results, result => result.Status == InitializeLedgerStatus.AlreadyExists);
        await using var readback = database.CreateContext();
        Assert.Equal(1, await readback.Ledgers.CountAsync());
        Assert.Equal(1, await readback.CashAccounts.CountAsync());
        Assert.NotNull(await new LedgerService(new LedgerRepository(readback), Dates).GetOverviewAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task ConcurrentAssetWrites_OneVersionWinsAndOtherTransactionRollsBack()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedGoldAsync(database);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await first.CashAccounts.LoadAsync();
        await first.GoldHoldings.LoadAsync();
        await second.CashAccounts.LoadAsync();
        await second.GoldHoldings.LoadAsync();

        var results = await Task.WhenAll(
            new GoldService(new LedgerRepository(first), Dates).BuyAsync(new(1m, 10m, 10m, Today, Today, null, Guid.NewGuid()), CancellationToken.None),
            new GoldService(new LedgerRepository(second), Dates).BuyAsync(new(1m, 10m, 10m, Today, Today, null, Guid.NewGuid()), CancellationToken.None));

        Assert.Single(results, result => result.Status == AssetOperationStatus.Created);
        Assert.Single(results, result => result.Status == AssetOperationStatus.Conflict);
        await using var readback = database.CreateContext();
        Assert.Equal(990m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(11m, (await readback.GoldHoldings.SingleAsync()).Grams);
        Assert.Equal(2, await readback.MutationReceipts.CountAsync());
        Assert.Equal(2, await readback.AssetTransactions.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentDateCorrections_CannotJointlyCreateNegativeHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid firstId;
        Guid secondId;
        await using (var seed = database.CreateContext())
        {
            var repository = new LedgerRepository(seed);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddDays(-20), 0m), CancellationToken.None);
            var entries = new LedgerEntryService(repository, Dates);
            firstId = (await entries.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today.AddDays(-20), null, Guid.NewGuid()), CancellationToken.None)).Result!.Entry.Id;
            secondId = (await entries.CreateAsync(new(LedgerEntryKind.Income, "工资", 100m, Today.AddDays(-20), null, Guid.NewGuid()), CancellationToken.None)).Result!.Entry.Id;
            await entries.CreateAsync(new(LedgerEntryKind.Expense, "餐饮", 100m, Today.AddDays(-19), null, Guid.NewGuid()), CancellationToken.None);
        }

        var barrier = new SaveBarrier();
        await using var first = database.CreateContext(barrier);
        await using var second = database.CreateContext(barrier);
        var results = await Task.WhenAll(
            new LedgerEntryService(new LedgerRepository(first), Dates).UpdateAsync(firstId, new(LedgerEntryKind.Income, "工资", 100m, Today.AddDays(-18), null, Guid.NewGuid()), CancellationToken.None),
            new LedgerEntryService(new LedgerRepository(second), Dates).UpdateAsync(secondId, new(LedgerEntryKind.Income, "工资", 100m, Today.AddDays(-18), null, Guid.NewGuid()), CancellationToken.None));

        Assert.Single(results, result => result.Status == EntryOperationStatus.Success);
        Assert.Single(results, result => result.Status == EntryOperationStatus.Conflict);
        await using var readback = database.CreateContext();
        Assert.Equal(100m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(100m, await readback.LedgerEntries.Where(entry => entry.Kind == LedgerEntryKind.Income && entry.BusinessDate == Today.AddDays(-20)).SumAsync(entry => entry.Amount));
    }

    [PostgresFact]
    public async Task DuplicateReceipt_RollsBackCashHoldingAndTransactionTogether()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedGoldAsync(database);
        await using (var write = database.CreateContext())
        {
            var cash = await write.CashAccounts.SingleAsync();
            var gold = await write.GoldHoldings.SingleAsync();
            var original = await write.MutationReceipts.AsNoTracking().SingleAsync();
            cash.ApplyCashEffect(-10m, Today, Now);
            gold.Buy(1m, 10m, 10m, Today, Now);
            write.AssetTransactions.Add(AssetTransaction.CreateGoldBuy(cash.LedgerId, 1m, 10m, 10m, Today, null, Guid.NewGuid(), Now));
            write.MutationReceipts.Add(MutationReceipt.Create(cash.LedgerId, original.RequestId, "duplicate", new string('0', 64), "{}", Now, null));

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new LedgerRepository(write).SaveChangesAsync(CancellationToken.None));
        }

        await using var readback = database.CreateContext();
        Assert.Equal(1000m, (await readback.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(10m, (await readback.GoldHoldings.SingleAsync()).Grams);
        Assert.Equal(1, await readback.AssetTransactions.CountAsync());
        Assert.Equal(1, await readback.MutationReceipts.CountAsync());
    }

    [PostgresFact]
    public async Task UpgradeFromPreviousMigration_PreservesLedgerBalanceAndAddsReceiptStore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(migrate: false);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021065135_AddAssetTransactionRequestId");
        var ledgerId = Guid.NewGuid();
        await InsertLegacyLedgerAsync(context, ledgerId);
        var cashId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cash_accounts (id, ledger_id, balance, as_of_date, updated_at, version) VALUES ({cashId}, {ledgerId}, {1234.56m}, {Today}, {Now}, {0})");

        await migrator.MigrateAsync();

        Assert.Equal(1234.56m, (await context.CashAccounts.SingleAsync()).Balance);
        Assert.Equal(1, (await context.Ledgers.SingleAsync()).SingletonSlot);
        Assert.Empty(await context.MutationReceipts.ToListAsync());
        Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [PostgresFact]
    public async Task UpgradeWithDuplicateLegacyLedgers_RefusesWithoutDeletingData()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(migrate: false);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021065135_AddAssetTransactionRequestId");
        await InsertLegacyLedgerAsync(context, Guid.NewGuid());
        await InsertLegacyLedgerAsync(context, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());

        Assert.Contains("multiple ledgers", exception.MessageText);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM ledgers", connection);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
        Assert.Equal(5, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    private static async Task SeedGoldAsync(PostgresTestDatabase database)
    {
        await using var context = database.CreateContext();
        var repository = new LedgerRepository(context);
        await new LedgerService(repository, Dates).InitializeAsync(new(Today, 1000m), CancellationToken.None);
        var result = await new GoldService(repository, Dates).SaveHoldingAsync(new(10m, 100m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today), CancellationToken.None);
        Assert.Equal(AssetOperationStatus.Success, result.Status);
    }

    private static Task InsertLegacyLedgerAsync(AppDbContext context, Guid id)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ledgers (id, currency, time_zone, start_date, created_at, updated_at) VALUES ({id}, {"CNY"}, {"Asia/Shanghai"}, {Today}, {Now}, {Now})");
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _ready.TrySetResult();
            }

            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class InitializeBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("EXISTS") && command.CommandText.Contains("ledgers"))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                {
                    _ready.TrySetResult();
                }

                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }
}
