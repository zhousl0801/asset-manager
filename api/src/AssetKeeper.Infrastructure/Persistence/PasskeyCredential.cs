namespace AssetKeeper.Infrastructure.Persistence;

/// <summary>
/// 已注册的 Passkey（WebAuthn 凭据）。仅用于本人登录，不参与账本数据。
/// </summary>
public sealed class PasskeyCredential
{
    public Guid Id { get; set; }

    public byte[] CredentialId { get; set; } = [];

    public byte[] PublicKey { get; set; } = [];

    public uint SignCount { get; set; }

    public string Label { get; set; } = "此设备";

    public string? Transports { get; set; }

    public bool BackupEligible { get; set; }

    public Guid AaGuid { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
