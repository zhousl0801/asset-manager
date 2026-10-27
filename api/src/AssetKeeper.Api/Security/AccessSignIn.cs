using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AssetKeeper.Api.Security;

public static class AccessSignIn
{
    public static Task SignInAsync(HttpContext context, AccessCredential credential)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "owner"),
                new Claim(AccessClaims.CredentialFingerprint, credential.Fingerprint)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
