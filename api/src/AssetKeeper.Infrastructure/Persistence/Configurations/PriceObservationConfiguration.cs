using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class PriceObservationConfiguration : IEntityTypeConfiguration<PriceObservation>
{
    public void Configure(EntityTypeBuilder<PriceObservation> builder)
    {
        builder.ToTable("price_observations");
        builder.HasKey(price => price.Id);
        builder.Property(price => price.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(price => price.LedgerId).HasColumnName("ledger_id");
        builder.Property(price => price.AssetType).HasColumnName("asset_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(price => price.FundPositionId).HasColumnName("fund_position_id");
        builder.Property(price => price.StockPositionId).HasColumnName("stock_position_id");
        builder.Property(price => price.SourceTransactionId).HasColumnName("source_transaction_id");
        builder.Property(price => price.Price).HasColumnName("price").HasPrecision(18, 4);
        builder.Property(price => price.EffectiveDate).HasColumnName("effective_date").HasColumnType("date");
        builder.Property(price => price.Source).HasColumnName("source").HasMaxLength(40).IsRequired();
        builder.Property(price => price.ObtainedAt).HasColumnName("obtained_at").HasColumnType("timestamptz");
        builder.Property(price => price.RevisionOrder).HasColumnName("revision_order");
        builder.Property(price => price.IsSuperseded).HasColumnName("is_superseded");
        builder.HasIndex(price => new { price.LedgerId, price.AssetType, price.FundPositionId, price.EffectiveDate });
        builder.HasIndex(price => new { price.LedgerId, price.AssetType, price.StockPositionId, price.EffectiveDate });
        builder.HasOne<Ledger>().WithMany().HasForeignKey(price => price.LedgerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FundPosition>().WithMany().HasForeignKey(price => price.FundPositionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StockPosition>().WithMany().HasForeignKey(price => price.StockPositionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetTransaction>().WithMany().HasForeignKey(price => price.SourceTransactionId).OnDelete(DeleteBehavior.Cascade);
    }
}
