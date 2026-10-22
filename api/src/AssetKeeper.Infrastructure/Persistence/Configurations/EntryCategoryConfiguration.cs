using AssetKeeper.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class EntryCategoryConfiguration : IEntityTypeConfiguration<EntryCategory>
{
    public void Configure(EntityTypeBuilder<EntryCategory> builder)
    {
        builder.ToTable("entry_categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(category => category.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(category => category.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(EntryCategory.MaximumNameLength)
            .IsRequired();

        builder.Property(category => category.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(category => category.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(category => category.SystemKey)
            .HasColumnName("system_key")
            .HasMaxLength(EntryCategory.MaximumSystemKeyLength);

        builder.Property(category => category.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(category => category.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(category => category.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(category => new { category.LedgerId, category.Kind, category.Name })
            .IsUnique();

        builder.HasIndex(category => new { category.LedgerId, category.Kind, category.SortOrder });

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(category => category.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
