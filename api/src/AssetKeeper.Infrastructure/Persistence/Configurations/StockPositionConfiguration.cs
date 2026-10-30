using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class StockPositionConfiguration : IEntityTypeConfiguration<StockPosition>
{
    public void Configure(EntityTypeBuilder<StockPosition> builder)
    {
        builder.ToTable("stock_positions");

        builder.HasKey(position => position.Id);

        builder.Property(position => position.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(position => position.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(position => position.Market)
            .HasColumnName("market")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(position => position.Code)
            .HasColumnName("code")
            .HasMaxLength(StockPosition.MaximumCodeLength)
            .IsRequired();

        builder.Property(position => position.Name)
            .HasColumnName("name")
            .HasMaxLength(StockPosition.MaximumNameLength)
            .IsRequired();

        builder.Property(position => position.Currency)
            .HasColumnName("currency")
            .HasMaxLength(StockPosition.MaximumCurrencyLength)
            .IsRequired();

        builder.Property(position => position.Shares)
            .HasColumnName("shares")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(position => position.Cost)
            .HasColumnName("cost")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(position => position.Price)
            .HasColumnName("price")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(position => position.PriceDate)
            .HasColumnName("price_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(position => position.PreviousClose)
            .HasColumnName("previous_close")
            .HasPrecision(18, 4);

        builder.Property(position => position.PreviousCloseDate)
            .HasColumnName("previous_close_date")
            .HasColumnType("date");

        builder.Property(position => position.RealizedProfit)
            .HasColumnName("realized_profit")
            .HasPrecision(18, 2)
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
        builder.HasIndex(position => new { position.LedgerId, position.Market, position.Code })
            .IsUnique();

        builder.HasOne(position => position.Ledger)
            .WithMany()
            .HasForeignKey(position => position.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
