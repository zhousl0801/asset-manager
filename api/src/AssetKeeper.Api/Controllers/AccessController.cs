using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController(IConfiguration configuration) : ControllerBase
{
    [HttpPost("verify")]
    public IActionResult Verify([FromBody] VerifyAccessRequest request)
    {
        var expected = configuration["ASSET_KEEPER_ACCESS_PASSWORD"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { title = "访问口令尚未配置" });
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(request.Password ?? string.Empty);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes)
                ? Ok(new { valid = true })
                : Unauthorized(new { title = "访问口令错误" });
    }
}

public sealed record VerifyAccessRequest(string? Password);
