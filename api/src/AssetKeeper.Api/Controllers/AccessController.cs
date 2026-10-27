using AssetKeeper.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController(AccessCredential credential, PasskeyService passkeys) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            required = credential.IsConfigured,
            authenticated = User.Identity?.IsAuthenticated == true,
            passkeyEnabled = passkeys.Enabled,
            passkeyAvailable = passkeys.Enabled && await passkeys.HasCredentialsAsync(cancellationToken)
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

        await AccessSignIn.SignInAsync(HttpContext, credential);

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
