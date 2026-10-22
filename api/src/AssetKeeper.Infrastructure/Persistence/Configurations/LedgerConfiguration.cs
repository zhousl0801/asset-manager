using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    public void Configure(EntityTypeBuilder<Ledger> builder)
    {
        builder.ToTable("ledgers", table => table.HasCheckConstraint("ck_ledgers_singleton_slot", "singleton_slot = 1"));

        builder.Property(ledger => ledger.SingletonSlot).HasColumnName("singleton_slot").HasDefaultValue(1);
        builder.HasIndex(ledger => ledger.SingletonSlot).IsUnique();

        builder.Property(ledger => ledger.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(ledger => ledger.NextFactSequence).HasColumnName("next_fact_sequence");

        builder.HasKey(ledger => ledger.Id);

        builder.Property(ledger => ledger.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ledger => ledger.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(ledger => ledger.TimeZone)
            .HasColumnName("time_zone")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(ledger => ledger.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(ledger => ledger.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(ledger => ledger.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
