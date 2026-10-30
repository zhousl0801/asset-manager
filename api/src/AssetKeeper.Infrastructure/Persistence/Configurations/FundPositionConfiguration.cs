using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class FundPositionConfiguration : IEntityTypeConfiguration<FundPosition>
{
    public void Configure(EntityTypeBuilder<FundPosition> builder)
    {
        builder.ToTable("fund_positions");

        builder.HasKey(position => position.Id);

        builder.Property(position => position.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(position => position.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(position => position.FundGroupId)
            .HasColumnName("fund_group_id");

        builder.Property(position => position.Name)
            .HasColumnName("name")
            .HasMaxLength(FundPosition.MaximumNameLength)
            .IsRequired();

        builder.Property(position => position.Code)
            .HasColumnName("code")
            .HasMaxLength(FundPosition.MaximumCodeLength);

        builder.Property(position => position.ShareClass)
            .HasColumnName("share_class")
            .HasMaxLength(FundPosition.MaximumShareClassLength);

        builder.Property(position => position.Platform)
            .HasColumnName("platform")
            .HasMaxLength(FundPosition.MaximumPlatformLength);

        builder.Property(position => position.Shares)
            .HasColumnName("shares")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(position => position.Cost)
            .HasColumnName("cost")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(position => position.NetValue)
            .HasColumnName("net_value")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(position => position.NetValueDate)
            .HasColumnName("net_value_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(position => position.RealizedProfit)
            .HasColumnName("realized_profit")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(position => position.PriorRealizedProfit)
            .HasColumnName("prior_realized_profit")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(position => position.ConfirmationDelayDays)
            .HasColumnName("confirmation_delay_days")
            .HasDefaultValue(2)
            .IsRequired();

        builder.Property(position => position.LastQuoteAttemptAt)
            .HasColumnName("last_quote_attempt_at")
            .HasColumnType("timestamptz");

        builder.Property(position => position.LastQuoteSource)
            .HasColumnName("last_quote_source")
            .HasMaxLength(AssetTransaction.MaximumQuoteSourceLength);

        builder.Property(position => position.LastQuoteError)
            .HasColumnName("last_quote_error")
            .HasMaxLength(300);

        builder.Property(position => position.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(position => position.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(position => position.LedgerId);
        builder.HasIndex(position => position.FundGroupId);

        builder.HasOne(position => position.Ledger)
            .WithMany()
            .HasForeignKey(position => position.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(position => position.FundGroup)
            .WithMany()
            .HasForeignKey(position => position.FundGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
