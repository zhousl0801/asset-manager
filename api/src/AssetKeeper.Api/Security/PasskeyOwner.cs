namespace AssetKeeper.Api.Security;

public static class PasskeyOwner
{
    /// <summary>
    /// 单用户应用的固定 WebAuthn 用户标识，用于支持无用户名（discoverable credential）登录。
    /// </summary>
    public static readonly byte[] UserHandle = System.Security.Cryptography.SHA256
        .HashData(System.Text.Encoding.UTF8.GetBytes("asset-keeper-owner"))[..16];

    public const string DisplayName = "资产管家";
}
