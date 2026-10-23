using System.Net;
using System.Net.Http.Json;
using SolarOps.Api.Features.Auth;

namespace SolarOps.Api.Tests.Integration.Auth;

/// <summary>Helpers calling the auth endpoints.</summary>
internal static class AuthApi
{
    public const string ValidPassword = "Solar-Panel-2026";

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    public static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/logout", new { refreshToken });

    /// <summary>Registers (if needed) and logs in; fails the test on any non-success status.</summary>
    public static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client, string email, string password = ValidPassword)
    {
        var register = await RegisterAsync(client, email, password);
        Assert.True(register.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"Register returned {register.StatusCode}");

        var login = await LoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";
}
