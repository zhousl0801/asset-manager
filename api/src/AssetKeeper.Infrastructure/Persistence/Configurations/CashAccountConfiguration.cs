using AssetKeeper.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class CashAccountConfiguration : IEntityTypeConfiguration<CashAccount>
{
    public void Configure(EntityTypeBuilder<CashAccount> builder)
    {
        builder.ToTable("cash_accounts");

        builder.Property(account => account.OpeningBalance).HasColumnName("opening_balance").HasPrecision(18, 2);
        builder.Property(account => account.OpeningDate).HasColumnName("opening_date").HasColumnType("date");
        builder.Property(account => account.HistoryConfirmed).HasColumnName("history_confirmed");

        builder.HasKey(cashAccount => cashAccount.Id);

        builder.Property(cashAccount => cashAccount.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cashAccount => cashAccount.LedgerId)
            .HasColumnName("ledger_id")
            .IsRequired();

        builder.Property(cashAccount => cashAccount.Balance)
            .HasColumnName("balance")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(cashAccount => cashAccount.AsOfDate)
            .HasColumnName("as_of_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(cashAccount => cashAccount.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(cashAccount => cashAccount.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(cashAccount => cashAccount.LedgerId)
            .IsUnique();

        builder.HasOne(cashAccount => cashAccount.Ledger)
            .WithOne(ledger => ledger.CashAccount)
            .HasForeignKey<CashAccount>(cashAccount => cashAccount.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
