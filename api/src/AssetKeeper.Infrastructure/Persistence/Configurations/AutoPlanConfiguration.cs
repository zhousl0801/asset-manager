using AssetKeeper.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class AutoPlanConfiguration : IEntityTypeConfiguration<AutoPlan>
{
    public void Configure(EntityTypeBuilder<AutoPlan> builder)
    {
        builder.ToTable("auto_plans");

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(plan => plan.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(plan => plan.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(plan => plan.Name)
            .HasColumnName("name")
            .HasMaxLength(AutoPlan.MaximumNameLength)
            .IsRequired();

        builder.Property(plan => plan.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(plan => plan.Fee)
            .HasColumnName("fee")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(plan => plan.Cycle)
            .HasColumnName("cycle")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(plan => plan.DayOfMonth)
            .HasColumnName("day_of_month");

        builder.Property(plan => plan.DayOfWeek)
            .HasColumnName("day_of_week");

        builder.Property(plan => plan.Time)
            .HasColumnName("time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(plan => plan.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(plan => plan.GenerateFrom)
            .HasColumnName("generate_from")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(plan => plan.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(plan => plan.FundPositionId)
            .HasColumnName("fund_position_id");

        builder.Property(plan => plan.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(plan => plan.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(plan => plan.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(plan => plan.LedgerId);
        builder.HasIndex(plan => new { plan.LedgerId, plan.Type });

        builder.HasOne<AssetKeeper.Domain.Ledgers.Ledger>()
            .WithMany()
            .HasForeignKey(plan => plan.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
