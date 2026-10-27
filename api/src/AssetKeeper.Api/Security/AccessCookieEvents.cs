using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AssetKeeper.Api.Security;

public static class AccessClaims
{
    public const string CredentialFingerprint = "access:credential";
}

/// <summary>
/// 校验会话 Cookie 里的口令指纹：口令变更后所有旧会话立即失效；API 未登录返回 401 而不是重定向。
/// </summary>
public sealed class AccessCookieEvents(AccessCredential credential) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var fingerprint = context.Principal?.FindFirstValue(AccessClaims.CredentialFingerprint);

        if (!credential.IsConfigured || !string.Equals(fingerprint, credential.Fingerprint, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
