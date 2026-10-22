using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class MutationReceiptConfiguration : IEntityTypeConfiguration<MutationReceipt>
{
    public void Configure(EntityTypeBuilder<MutationReceipt> builder)
    {
        builder.ToTable("mutation_receipts");
        builder.HasKey(receipt => new { receipt.LedgerId, receipt.RequestId });
        builder.Property(receipt => receipt.LedgerId).HasColumnName("ledger_id");
        builder.Property(receipt => receipt.RequestId).HasColumnName("request_id");
        builder.Property(receipt => receipt.Operation).HasColumnName("operation").HasMaxLength(64).IsRequired();
        builder.Property(receipt => receipt.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(receipt => receipt.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb").IsRequired();
        builder.Property(receipt => receipt.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(receipt => receipt.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.HasOne<Ledger>().WithMany().HasForeignKey(receipt => receipt.LedgerId).OnDelete(DeleteBehavior.Cascade);
    }
}
