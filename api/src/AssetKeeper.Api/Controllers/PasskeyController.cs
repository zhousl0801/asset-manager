using System.Text.Json;
using System.Text.Json.Nodes;
using AssetKeeper.Api.Security;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/access/passkeys")]
public sealed class PasskeyController(
    PasskeyService passkeys,
    PasskeyChallenges challenges,
    AccessCredential access,
    ILogger<PasskeyController> logger) : ControllerBase
{
    [Authorize]
    [HttpPost("registration/options")]
    public async Task<IActionResult> RegistrationOptions(CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        var options = await passkeys.CreateRegistrationOptionsAsync(cancellationToken);
        challenges.Store(Response, options);
        return PasskeyJson(options);
    }

    [Authorize]
    [HttpPost("registration/complete")]
    public async Task<IActionResult> RegistrationComplete(
        [FromBody] PasskeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        var options = challenges.Take<CredentialCreateOptions>(Request);
        challenges.Clear(Response);

        if (options is null)
        {
            return BadRequest(new { title = "注册已超时，请重新开始。" });
        }

        try
        {
            var credential = await passkeys.CompleteRegistrationAsync(
                Deserialize<AuthenticatorAttestationRawResponse>(request.Response),
                options,
                request.Label,
                cancellationToken);

            return Ok(new { credential.Id, credential.Label, credential.CreatedAt });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Passkey 注册校验失败。");
            return BadRequest(new { title = "Passkey 注册未通过，请重试；如仍失败，请使用访问口令登录。" });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting(AccessRateLimiting.LoginPolicy)]
    [HttpPost("login/options")]
    public async Task<IActionResult> LoginOptions(CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        if (!await passkeys.HasCredentialsAsync(cancellationToken))
        {
            return BadRequest(new { title = "尚未注册 Passkey，请先用访问口令登录并在此设备启用。" });
        }

        var options = await passkeys.CreateAssertionOptionsAsync(cancellationToken);
        challenges.Store(Response, options);
        return PasskeyJson(options);
    }

    [AllowAnonymous]
    [EnableRateLimiting(AccessRateLimiting.LoginPolicy)]
    [HttpPost("login/complete")]
    public async Task<IActionResult> LoginComplete(
        [FromBody] PasskeyLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        var options = challenges.Take<AssertionOptions>(Request);
        challenges.Clear(Response);

        if (options is null)
        {
            return BadRequest(new { title = "登录已超时，请重新尝试。" });
        }

        try
        {
            await passkeys.CompleteAssertionAsync(
                Deserialize<AuthenticatorAssertionRawResponse>(request.Response),
                options,
                cancellationToken);

            await AccessSignIn.SignInAsync(HttpContext, access);
            return Ok(new { authenticated = true });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Passkey 登录校验失败。");
            return Unauthorized(new { title = "Passkey 验证未通过，请重试或改用访问口令。" });
        }
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        var credentials = await passkeys.ListAsync(cancellationToken);
        return Ok(credentials.Select(credential => new
        {
            credential.Id,
            credential.Label,
            credential.CreatedAt,
            credential.LastUsedAt
        }));
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!passkeys.Enabled)
        {
            return PasskeyNotConfigured();
        }

        return await passkeys.DeleteAsync(id, cancellationToken)
            ? NoContent()
            : NotFound(new { title = "未找到该 Passkey。" });
    }

    private IActionResult PasskeyJson(object payload)
    {
        // 不能走 MVC 的全局枚举转字符串策略：COSE 算法必须以 COSE 数字标识（如 -7）下发。
        return Content(JsonSerializer.Serialize(payload, PasskeyJsonOptions), "application/json");
    }

    private static readonly JsonSerializerOptions PasskeyJsonOptions = new(JsonSerializerDefaults.Web);

    private ObjectResult PasskeyNotConfigured()
    {
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new { title = "服务器未启用 Passkey，请使用访问口令登录。" });
    }

    private static T Deserialize<T>(JsonElement element) where T : class
    {
        // 浏览器可能省略 transports / clientExtensionResults，补齐后再交给 Fido2NetLib 校验。
        var node = JsonNode.Parse(element.GetRawText())!.AsObject();

        if (node["response"] is JsonObject response && response["transports"] is null)
        {
            response["transports"] = new JsonArray();
        }

        node["clientExtensionResults"] ??= new JsonObject();

        return node.Deserialize<T>()
            ?? throw new JsonException("Passkey 响应为空。");
    }
}

public sealed record PasskeyRegistrationRequest(JsonElement Response, string? Label);

public sealed record PasskeyLoginRequest(JsonElement Response);
