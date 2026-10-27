using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AssetKeeper.Tests.Api;

public sealed class PasskeyEndpointsTests
{
    [Fact]
    public async Task Session_ReportsPasskeysEnabledButUnavailable()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var session = await client.GetFromJsonAsync<JsonElement>("/api/access/session");

        Assert.True(session.GetProperty("passkeyEnabled").GetBoolean());
        Assert.False(session.GetProperty("passkeyAvailable").GetBoolean());
    }

    [Fact]
    public async Task RegistrationOptions_RequireSession()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync("/api/access/passkeys/registration/options", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationOptions_WithSession_ReturnChallengeAndFlowCookie()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var response = await client.PostAsync("/api/access/passkeys/registration/options", null);
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(options.GetProperty("challenge").GetString()));
        Assert.Equal("localhost", options.GetProperty("rp").GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(options.GetProperty("user").GetProperty("id").GetString()));
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith("asset-keeper-passkey=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistrationComplete_WithoutChallengeCookie_ReturnsBadRequest()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var response = await client.PostAsJsonAsync(
            "/api/access/passkeys/registration/complete",
            new { response = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginOptions_WithoutRegisteredPasskey_ReturnsBadRequest()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync("/api/access/passkeys/login/options", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WithSession_ReturnsEmptyList()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var credentials = await client.GetFromJsonAsync<JsonElement>("/api/access/passkeys");

        Assert.Equal(JsonValueKind.Array, credentials.ValueKind);
        Assert.Equal(0, credentials.GetArrayLength());
    }

    [Fact]
    public async Task Delete_UnknownPasskey_ReturnsNotFound()
    {
        using var factory = new AccessApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var response = await client.DeleteAsync($"/api/access/passkeys/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_ReturnServiceUnavailable_WhenPasskeysAreNotConfigured()
    {
        using var factory = new AccessApiFactory(AccessApiFactory.Password, configurePasskeys: false);
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/access/login", new { password = AccessApiFactory.Password });

        var session = await client.GetFromJsonAsync<JsonElement>("/api/access/session");
        var response = await client.PostAsync("/api/access/passkeys/registration/options", null);

        Assert.False(session.GetProperty("passkeyEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
