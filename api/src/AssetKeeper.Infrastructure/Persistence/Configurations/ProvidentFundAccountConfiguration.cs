using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class ProvidentFundAccountConfiguration : IEntityTypeConfiguration<ProvidentFundAccount>
{
    public void Configure(EntityTypeBuilder<ProvidentFundAccount> builder)
    {
        builder.ToTable("provident_fund_accounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(account => account.Balance)
            .HasColumnName("balance")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(account => account.AsOfDate)
            .HasColumnName("as_of_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(account => account.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(account => account.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(account => account.LedgerId)
            .IsUnique();

        builder.HasOne(account => account.Ledger)
            .WithOne()
            .HasForeignKey<ProvidentFundAccount>(account => account.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
