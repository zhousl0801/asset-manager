using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class GoldHoldingConfiguration : IEntityTypeConfiguration<GoldHolding>
{
    public void Configure(EntityTypeBuilder<GoldHolding> builder)
    {
        builder.ToTable("gold_holdings");

        builder.HasKey(holding => holding.Id);

        builder.Property(holding => holding.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(holding => holding.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(holding => holding.Grams)
            .HasColumnName("grams")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(holding => holding.Cost)
            .HasColumnName("cost")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(holding => holding.Quote)
            .HasColumnName("quote")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(holding => holding.QuoteDate)
            .HasColumnName("quote_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(holding => holding.RealizedProfit)
            .HasColumnName("realized_profit")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(holding => holding.LastQuoteAttemptAt)
            .HasColumnName("last_quote_attempt_at")
            .HasColumnType("timestamptz");

        builder.Property(holding => holding.LastQuoteSource)
            .HasColumnName("last_quote_source")
            .HasMaxLength(AssetTransaction.MaximumQuoteSourceLength);

        builder.Property(holding => holding.LastQuoteError)
            .HasColumnName("last_quote_error")
            .HasMaxLength(300);

        builder.Property(holding => holding.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(holding => holding.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(holding => holding.LedgerId)
            .IsUnique();

        builder.HasOne(holding => holding.Ledger)
            .WithOne()
            .HasForeignKey<GoldHolding>(holding => holding.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
