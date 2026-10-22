using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.Property(entry => entry.BusinessSequence).HasColumnName("business_sequence");
        builder.Property(entry => entry.RelatedCashAdjustmentId).HasColumnName("related_cash_adjustment_id");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(entry => entry.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Category)
            .HasColumnName("category")
            .HasMaxLength(LedgerEntry.MaximumCategoryLength)
            .IsRequired();

        builder.Property(entry => entry.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.BusinessDate)
            .HasColumnName("business_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(entry => entry.Note)
            .HasColumnName("note")
            .HasMaxLength(LedgerEntry.MaximumNoteLength);

        builder.Property(entry => entry.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.RequestId)
            .HasColumnName("request_id")
            .IsRequired();

        builder.Property(entry => entry.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(entry => entry.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(entry => entry.ReversedAt)
            .HasColumnName("reversed_at")
            .HasColumnType("timestamptz");

        builder.Property(entry => entry.ReversalReason)
            .HasColumnName("reversal_reason")
            .HasMaxLength(LedgerEntry.MaximumNoteLength);

        builder.Property(entry => entry.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(entry => new { entry.LedgerId, entry.BusinessDate });

        builder.HasIndex(entry => new { entry.LedgerId, entry.RequestId })
            .IsUnique();

        builder.HasOne(entry => entry.Ledger)
            .WithMany()
            .HasForeignKey(entry => entry.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
