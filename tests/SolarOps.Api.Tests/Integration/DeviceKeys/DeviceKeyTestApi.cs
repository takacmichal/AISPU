using SolarOps.Api.Features.DeviceKeys;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.DeviceKeys;

/// <summary>Helpers for tests that call the API as a device holding an installation API key.</summary>
internal static class DeviceKeyTestApi
{
    public static string KeysUrl(Guid installationId, string suffix = "") => $"/api/installations/{installationId}/api-keys{suffix}";

    /// <summary>Issues the installation key as an Admin and returns the plaintext.</summary>
    public static async Task<IssuedDeviceApiKeyResponse> IssueKeyAsync(this ApiFactory factory, Guid installationId)
    {
        using var admin = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);
        using var response = await admin.PostAsync(KeysUrl(installationId), content: null);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<IssuedDeviceApiKeyResponse>();
    }

    /// <summary>Anonymous client sending only <c>X-Api-Key</c> (no user, as a real device would).</summary>
    public static HttpClient CreateDeviceClient(this ApiFactory factory, string apiKey)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(DeviceApiKeyDefaults.HeaderName, apiKey);
        return client;
    }
}
