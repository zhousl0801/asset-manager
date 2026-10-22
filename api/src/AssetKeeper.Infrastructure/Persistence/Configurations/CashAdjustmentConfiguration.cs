using AssetKeeper.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class CashAdjustmentConfiguration : IEntityTypeConfiguration<CashAdjustment>
{
    public void Configure(EntityTypeBuilder<CashAdjustment> builder)
    {
        builder.ToTable("cash_adjustments");

        builder.Property(adjustment => adjustment.BusinessSequence).HasColumnName("business_sequence");
        builder.Property(adjustment => adjustment.RecordedDifference).HasColumnName("recorded_difference").HasPrecision(18, 2);
        builder.Property(adjustment => adjustment.IsReversed).HasColumnName("is_reversed");
        builder.Property(adjustment => adjustment.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasKey(adjustment => adjustment.Id);

        builder.Property(adjustment => adjustment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(adjustment => adjustment.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(adjustment => adjustment.PreviousBalance)
            .HasColumnName("previous_balance")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(adjustment => adjustment.NewBalance)
            .HasColumnName("new_balance")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(adjustment => adjustment.Difference)
            .HasColumnName("difference")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(adjustment => adjustment.BusinessDate)
            .HasColumnName("business_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(adjustment => adjustment.Reason)
            .HasColumnName("reason")
            .HasMaxLength(CashAdjustment.MaximumReasonLength)
            .IsRequired();

        builder.Property(adjustment => adjustment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(adjustment => new { adjustment.LedgerId, adjustment.BusinessDate });

        builder.HasOne(adjustment => adjustment.Ledger)
            .WithMany()
            .HasForeignKey(adjustment => adjustment.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
