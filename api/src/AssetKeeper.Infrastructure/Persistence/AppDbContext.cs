using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    public DbSet<MutationReceipt> MutationReceipts => Set<MutationReceipt>();

    public DbSet<Ledger> Ledgers => Set<Ledger>();

    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<CashAdjustment> CashAdjustments => Set<CashAdjustment>();

    public DbSet<ProvidentFundAccount> ProvidentFundAccounts => Set<ProvidentFundAccount>();

    public DbSet<FundPosition> FundPositions => Set<FundPosition>();

    public DbSet<GoldHolding> GoldHoldings => Set<GoldHolding>();

    public DbSet<AssetTransaction> AssetTransactions => Set<AssetTransaction>();

    public DbSet<AutoPlan> AutoPlans => Set<AutoPlan>();

    public DbSet<PlanOccurrence> PlanOccurrences => Set<PlanOccurrence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
