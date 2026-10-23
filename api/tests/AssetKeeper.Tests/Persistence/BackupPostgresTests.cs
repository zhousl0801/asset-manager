using AssetKeeper.Application.Backup;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Infrastructure.Backup;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Persistence;

public sealed class BackupPostgresTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly BusinessDateProvider Dates = new(new FixedTimeProvider(new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero)));

    [PostgresFact]
    public async Task RestoreFailure_RollsBackAndKeepsCurrentLedger()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            var repository = new LedgerRepository(context);
            await new LedgerService(repository, Dates).InitializeAsync(new(Today.AddMonths(-1), 10000m), CancellationToken.None);
            await new LedgerEntryService(repository, Dates).CreateAsync(
                new CreateLedgerEntryRequest(LedgerEntryKind.Income, "工资", 1000m, Today, "工资到账", Guid.NewGuid()),
                CancellationToken.None);
        }

        BackupDocument backup;
        string token;

        await using (var context = database.CreateContext())
        {
            var service = new BackupService(context, Dates);
            backup = (await service.ExportAsync(CancellationToken.None))!;
            var category = backup.Categories.Single(item => item.Name == "工资" && item.Kind == "Income");
            var tooLong = new string('类', 30);
            category.Name = tooLong;
            backup.Entries.Single().Category = tooLong;
            var validation = await service.ValidateAsync(backup, CancellationToken.None);
            Assert.True(validation.IsValid);
            token = validation.ValidationToken!;
        }

        await using (var context = database.CreateContext())
        {
            var service = new BackupService(context, Dates);

            await Assert.ThrowsAnyAsync<Exception>(() => service.RestoreAsync(
                new RestoreBackupRequest(backup, token, true),
                CancellationToken.None));
        }

        await using (var readback = database.CreateContext())
        {
            Assert.Equal(1, await readback.Ledgers.CountAsync());
            var entry = await readback.LedgerEntries.SingleAsync();
            Assert.Equal("工资", entry.Category);
            Assert.Equal(11000m, (await readback.CashAccounts.SingleAsync()).Balance);
            Assert.Equal(18, await readback.EntryCategories.CountAsync());
        }
    }
}
