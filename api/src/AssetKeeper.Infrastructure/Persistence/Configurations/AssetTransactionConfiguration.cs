using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class AssetTransactionConfiguration : IEntityTypeConfiguration<AssetTransaction>
{
    public void Configure(EntityTypeBuilder<AssetTransaction> builder)
    {
        builder.ToTable("asset_transactions");

        builder.Property(transaction => transaction.BusinessSequence).HasColumnName("business_sequence");
        builder.Property(transaction => transaction.PriceDate).HasColumnName("price_date").HasColumnType("date");
        builder.Property(transaction => transaction.NeedsDateConfirmation).HasColumnName("needs_date_confirmation");
        builder.Property(transaction => transaction.IsReversed).HasColumnName("is_reversed");
        builder.Property(transaction => transaction.RelatedCashAdjustmentId).HasColumnName("related_cash_adjustment_id");
        builder.Property(transaction => transaction.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(transaction => transaction.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(transaction => transaction.RequestId)
            .HasColumnName("request_id")
            .IsRequired();

        builder.Property(transaction => transaction.AssetType)
            .HasColumnName("asset_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(transaction => transaction.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(transaction => transaction.FundPositionId)
            .HasColumnName("fund_position_id");

        builder.Property(transaction => transaction.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 4);

        builder.Property(transaction => transaction.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(transaction => transaction.CashEffect)
            .HasColumnName("cash_effect")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(transaction => transaction.Price)
            .HasColumnName("price")
            .HasPrecision(18, 4);

        builder.Property(transaction => transaction.Subject)
            .HasColumnName("subject")
            .HasMaxLength(AssetTransaction.MaximumSubjectLength);

        builder.Property(transaction => transaction.Note)
            .HasColumnName("note")
            .HasMaxLength(AssetTransaction.MaximumNoteLength);

        builder.Property(transaction => transaction.BusinessDate)
            .HasColumnName("business_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(transaction => transaction.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(transaction => new { transaction.LedgerId, transaction.BusinessDate });

        builder.HasIndex(transaction => new { transaction.LedgerId, transaction.AssetType, transaction.BusinessDate });

        builder.HasIndex(transaction => new { transaction.LedgerId, transaction.RequestId })
            .IsUnique();

        builder.HasOne(transaction => transaction.Ledger)
            .WithMany()
            .HasForeignKey(transaction => transaction.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FundPosition>()
            .WithMany()
            .HasForeignKey(transaction => transaction.FundPositionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
