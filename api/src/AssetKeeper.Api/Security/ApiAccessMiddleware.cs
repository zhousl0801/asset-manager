using System.Security.Cryptography;
using System.Text;

namespace AssetKeeper.Api.Security;

public sealed class ApiAccessMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string VerificationPath = "/api/access/verify";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/api/health")
            || string.Equals(context.Request.Path.Value, VerificationPath, StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(context.Request.Method)
            || !environment.IsProduction())
        {
            await next(context);
            return;
        }

        var expected = configuration["ASSET_KEEPER_ACCESS_PASSWORD"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { title = "访问口令尚未配置" });
            return;
        }

        var supplied = context.Request.Headers.Authorization.ToString();
        if (supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            supplied = supplied[7..];
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var valid = expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);

        if (!valid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "访问口令无效或已过期" });
            return;
        }

        await next(context);
    }
}
