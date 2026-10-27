using System.Security.Cryptography;
using System.Text;

namespace AssetKeeper.Api.Security;

/// <summary>
/// 访问口令的唯一来源。优先使用配置中的散列，未配置散列时才接受明文口令配置（进程内散列一次）。
/// </summary>
public sealed class AccessCredential
{
    public const string PasswordHashKey = "ASSET_KEEPER_ACCESS_PASSWORD_HASH";
    public const string PasswordKey = "ASSET_KEEPER_ACCESS_PASSWORD";

    private readonly string? _encodedHash;

    public AccessCredential(IConfiguration configuration)
    {
        var configuredHash = configuration[PasswordHashKey];
        var configuredPassword = configuration[PasswordKey];

        _encodedHash = !string.IsNullOrWhiteSpace(configuredHash)
            ? configuredHash.Trim()
            : !string.IsNullOrWhiteSpace(configuredPassword)
                ? AccessPasswordHasher.Hash(configuredPassword)
                : null;

        Fingerprint = _encodedHash is null
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_encodedHash)));
    }

    public bool IsConfigured => _encodedHash is not null;

    /// <summary>
    /// 由当前口令散列派生的指纹，写入会话 Cookie；改口令后旧会话自动失效。
    /// </summary>
    public string Fingerprint { get; }

    public bool Verify(string? password)
    {
        return IsConfigured && AccessPasswordHasher.Verify(password, _encodedHash);
    }
}
