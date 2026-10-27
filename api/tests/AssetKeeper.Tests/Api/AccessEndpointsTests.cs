using System.Net;
using System.Net.Http.Json;
using AssetKeeper.Api.Security;
using AssetKeeper.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssetKeeper.Tests.Api;

public sealed class AccessEndpointsTests
{
    [Fact]
    public async Task Session_WithoutCookie_ReportsRequiredAndUnauthenticated()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/access/session");

        Assert.NotNull(session);
        Assert.True(session.Required);
        Assert.False(session.Authenticated);
    }

    [Fact]
    public async Task DataEndpoints_WithoutCookie_ReturnUnauthorized()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Health_RemainsPublic()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedAndKeepsSessionClosed()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/access/login", new { password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectPassword_IssuesHttpOnlySessionCookie()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("asset-keeper-access=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/access/session");
        Assert.True(session?.Authenticated);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Logout_ClosesExistingSession()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var response = await client.PostAsync("/api/access/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Login_AfterPasswordChange_InvalidatesPreviousSession()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        var login = await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var rotated = new AccessApiFactory("a different password");
        using var rotatedClient = rotated.CreateHttpsClient();
        rotatedClient.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, (await rotatedClient.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Development_WithConfiguredPassword_StillRequiresSession()
    {
        using var factory = new AccessApiFactory("local password", "Development");
        using var client = factory.CreateHttpsClient();

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/access/session");
        Assert.NotNull(session);
        Assert.True(session.Required);
        Assert.False(session.Authenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);

        await client.PostAsJsonAsync("/api/access/login", new { password = "local password" });
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger")).StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 11; attempt++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/access/login", new { password = "wrong" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Contains(HttpStatusCode.Unauthorized, statuses);
    }

    private sealed record SessionResponse(bool Required, bool Authenticated);
}

public sealed class AccessApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "correct horse battery staple";

    private readonly string _databaseName = $"asset-keeper-access-{Guid.NewGuid()}";
    private readonly string _password;
    private readonly string _passwordHash;
    private readonly string _environment;
    private readonly bool _configurePasskeys;

    public AccessApiFactory() : this(Password) { }

    public AccessApiFactory(string password, string environment = "Production", bool configurePasskeys = true)
    {
        _password = password;
        _passwordHash = AccessPasswordHasher.Hash(password, 10_000);
        _environment = environment;
        _configurePasskeys = configurePasskeys;
    }

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["PlanScheduler:Enabled"] = "false",
                ["QuoteRefreshScheduler:Enabled"] = "false",
                [AccessCredential.PasswordHashKey] = _passwordHash,
                [AccessCredential.PasswordKey] = _password
            };

            if (_configurePasskeys)
            {
                settings["Passkey:RpId"] = "localhost";
                settings["Passkey:Origins:0"] = "https://localhost";
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
