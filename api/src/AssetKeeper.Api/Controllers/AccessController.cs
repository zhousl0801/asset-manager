using System.Security.Claims;
using AssetKeeper.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController(AccessCredential credential) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("session")]
    public IActionResult Session()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            required = credential.IsConfigured,
            authenticated = User.Identity?.IsAuthenticated == true
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting(AccessRateLimiting.LoginPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!credential.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { title = "访问口令尚未配置" });
        }

        if (!credential.Verify(request.Password))
        {
            return Unauthorized(new { title = "访问口令错误" });
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "owner"),
                new Claim(AccessClaims.CredentialFingerprint, credential.Fingerprint)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return Ok(new { authenticated = true });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public sealed record LoginRequest(string? Password);
