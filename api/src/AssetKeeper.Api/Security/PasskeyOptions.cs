using Fido2NetLib;

namespace AssetKeeper.Api.Security;

/// <summary>
/// Passkey 的依赖方（RP）配置。生产环境默认取 Render 提供的公网地址，本地开发使用 localhost。
/// </summary>
public sealed class PasskeyOptions
{
    public string RpId { get; init; } = string.Empty;

    public string[] Origins { get; init; } = [];

    public bool Enabled => !string.IsNullOrWhiteSpace(RpId) && Origins.Length > 0;

    public static PasskeyOptions Resolve(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredRpId = configuration["Passkey:RpId"];
        var configuredOrigins = configuration.GetSection("Passkey:Origins").Get<string[]>() ?? [];

        if (!string.IsNullOrWhiteSpace(configuredRpId) && configuredOrigins.Length > 0)
        {
            return new PasskeyOptions
            {
                RpId = configuredRpId.Trim(),
                Origins = configuredOrigins.Select(origin => origin.TrimEnd('/')).ToArray()
            };
        }

        // Render 会为 Web Service 注入公网地址；同源部署下它就是唯一的 Origin。
        var externalUrl = configuration["RENDER_EXTERNAL_URL"];
        if (!string.IsNullOrWhiteSpace(externalUrl)
            && Uri.TryCreate(externalUrl, UriKind.Absolute, out var uri))
        {
            return new PasskeyOptions
            {
                RpId = uri.Host,
                Origins = [externalUrl.TrimEnd('/')]
            };
        }

        if (environment.IsDevelopment())
        {
            return new PasskeyOptions
            {
                RpId = "localhost",
                Origins = ["http://localhost:5173", "http://localhost:5069"]
            };
        }

        return new PasskeyOptions();
    }

    public Fido2Configuration ToFido2Configuration()
    {
        return new Fido2Configuration
        {
            RPID = RpId,
            RPName = "资产管家",
            Origins = Origins.ToHashSet(StringComparer.OrdinalIgnoreCase),
            TimestampDriftTolerance = 300_000
        };
    }
}
