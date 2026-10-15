using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Installations;

/// <summary>Helpers shared by the Installations / Devices / Strings / GridConnection integration tests.</summary>
internal static class InstallationsTestData
{
    /// <summary>Same JSON settings as the API (web defaults + enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<T?> ReadAsync<T>(this HttpResponseMessage response) => response.Content.ReadFromJsonAsync<T>(Json);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static CreateInstallationRequest NewInstallationRequest(decimal? plannedPowerKwp = 9.6m) => new()
    {
        CustomerId = Guid.CreateVersion7(),
        ContractId = Guid.CreateVersion7(),
        SupplyPointId = Guid.CreateVersion7(),
        Name = "FVE Rodinný dom Nitra",
        PlannedPowerKwp = plannedPowerKwp,
    };

    public static async Task<InstallationDetailResponse> CreateInstallationAsync(this HttpClient client, CreateInstallationRequest? request = null)
    {
        var response = await client.PostJsonAsync("/api/installations", request ?? NewInstallationRequest());
        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<InstallationDetailResponse>())!;
    }

    /// <summary>Creates a user with role Technician directly in the Identity tables and returns its id.</summary>
    public static async Task<Guid> SeedTechnicianAsync(this ApiFactory factory)
    {
        var user = new AppUser { UserName = $"tech-{Guid.NewGuid():N}", Email = "tech@example.com" };
        await factory.ExecuteDbAsync(async db =>
        {
            var role = db.Set<IdentityRole<Guid>>().FirstOrDefault(r => r.Name == Roles.Technician);
            if (role is null)
            {
                role = new IdentityRole<Guid>(Roles.Technician) { Id = Guid.CreateVersion7(), NormalizedName = Roles.Technician.ToUpperInvariant() };
                db.Add(role);
            }

            db.Add(user);
            db.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();
        });
        return user.Id;
    }
}
