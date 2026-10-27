using Microsoft.Extensions.Caching.Memory;

namespace AssetKeeper.Api.Security;

/// <summary>
/// 把注册 / 登录的挑战暂存在服务端内存，用一次性 Cookie 关联，避免挑战暴露在客户端。
/// </summary>
public sealed class PasskeyChallenges(IMemoryCache cache, IWebHostEnvironment environment)
{
    public const string CookieName = "asset-keeper-passkey";
    private const string KeyPrefix = "passkey-flow:";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public void Store<T>(HttpResponse response, T payload) where T : class
    {
        var flowId = Guid.NewGuid().ToString("N");
        cache.Set(KeyPrefix + flowId, payload, Lifetime);

        response.Cookies.Append(CookieName, flowId, CookieOptions());
    }

    public T? Take<T>(HttpRequest request) where T : class
    {
        if (!request.Cookies.TryGetValue(CookieName, out var flowId)
            || string.IsNullOrWhiteSpace(flowId))
        {
            return null;
        }

        if (!cache.TryGetValue(KeyPrefix + flowId, out var payload) || payload is not T typed)
        {
            return null;
        }

        cache.Remove(KeyPrefix + flowId);
        return typed;
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, CookieOptions());
    }

    private CookieOptions CookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = environment.IsProduction(),
            Path = "/api/access/passkeys",
            MaxAge = Lifetime
        };
    }
}
