using AssetKeeper.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class FundGroupConfiguration : IEntityTypeConfiguration<FundGroup>
{
    public void Configure(EntityTypeBuilder<FundGroup> builder)
    {
        builder.ToTable("fund_groups");

        builder.HasKey(group => group.Id);

        builder.Property(group => group.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(group => group.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(group => group.Name)
            .HasColumnName("name")
            .HasMaxLength(FundGroup.MaximumNameLength)
            .IsRequired();

        builder.Property(group => group.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(group => group.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(group => group.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(group => group.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(group => new { group.LedgerId, group.Name })
            .IsUnique();

        builder.HasIndex(group => new { group.LedgerId, group.SortOrder });

        builder.HasOne(group => group.Ledger)
            .WithMany()
            .HasForeignKey(group => group.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
