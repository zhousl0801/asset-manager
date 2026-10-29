using AssetKeeper.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class PlanOccurrenceConfiguration : IEntityTypeConfiguration<PlanOccurrence>
{
    public void Configure(EntityTypeBuilder<PlanOccurrence> builder)
    {
        builder.ToTable("plan_occurrences");

        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(occurrence => occurrence.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.PeriodKey)
            .HasColumnName("period_key")
            .HasMaxLength(PlanOccurrence.MaximumPeriodKeyLength)
            .IsRequired();

        builder.Property(occurrence => occurrence.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(occurrence => occurrence.PlanName)
            .HasColumnName("plan_name")
            .HasMaxLength(AutoPlan.MaximumNameLength)
            .IsRequired();

        builder.Property(occurrence => occurrence.FundPositionId)
            .HasColumnName("fund_position_id");

        builder.Property(occurrence => occurrence.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(occurrence => occurrence.Fee)
            .HasColumnName("fee")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(occurrence => occurrence.PlanVersion)
            .HasColumnName("plan_version")
            .IsRequired();

        builder.Property(occurrence => occurrence.DueDate)
            .HasColumnName("due_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(occurrence => occurrence.DueTime)
            .HasColumnName("due_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(occurrence => occurrence.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(occurrence => occurrence.RelatedRecordId)
            .HasColumnName("related_record_id");

        builder.Property(occurrence => occurrence.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(PlanOccurrence.MaximumErrorLength);

        builder.Property(occurrence => occurrence.ExecutedAt)
            .HasColumnName("executed_at")
            .HasColumnType("timestamptz");

        builder.Property(occurrence => occurrence.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(occurrence => occurrence.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(occurrence => occurrence.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(occurrence => new { occurrence.PlanId, occurrence.PeriodKey }).IsUnique();
        builder.HasIndex(occurrence => new { occurrence.LedgerId, occurrence.DueDate, occurrence.Status });

        builder.HasOne<AutoPlan>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
