using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetKeeper.Infrastructure.Persistence.Configurations;

internal sealed class PasskeyCredentialConfiguration : IEntityTypeConfiguration<PasskeyCredential>
{
    public const int MaximumLabelLength = 60;
    public const int MaximumTransportsLength = 120;

    public void Configure(EntityTypeBuilder<PasskeyCredential> builder)
    {
        builder.ToTable("passkey_credentials");

        builder.HasKey(credential => credential.Id);

        builder.Property(credential => credential.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(credential => credential.CredentialId)
            .HasColumnName("credential_id")
            .IsRequired();

        builder.HasIndex(credential => credential.CredentialId)
            .IsUnique();

        builder.Property(credential => credential.PublicKey)
            .HasColumnName("public_key")
            .IsRequired();

        builder.Property(credential => credential.SignCount)
            .HasColumnName("sign_count")
            .IsRequired();

        builder.Property(credential => credential.Label)
            .HasColumnName("label")
            .HasMaxLength(MaximumLabelLength)
            .IsRequired();

        builder.Property(credential => credential.Transports)
            .HasColumnName("transports")
            .HasMaxLength(MaximumTransportsLength);

        builder.Property(credential => credential.BackupEligible)
            .HasColumnName("backup_eligible")
            .IsRequired();

        builder.Property(credential => credential.AaGuid)
            .HasColumnName("aaguid")
            .IsRequired();

        builder.Property(credential => credential.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(credential => credential.LastUsedAt)
            .HasColumnName("last_used_at")
            .HasColumnType("timestamptz");
    }
}
