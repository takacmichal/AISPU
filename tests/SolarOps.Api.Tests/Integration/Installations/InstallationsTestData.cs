using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Unit.Installations;

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

    /// <summary>
    /// Adds a connection request of the installation in state <paramref name="state"/> (default: approved, i.e. a contract
    /// was offered). Planning (#88) and commissioning (#92) require an approved one.
    /// </summary>
    public static async Task SeedConnectionRequestAsync(
        this ApiFactory factory, InstallationResponse installation, ConnectionRequestState state = ConnectionRequestState.ContractOffered)
    {
        await factory.ExecuteDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var request = ConnectionRequest.Create(
                installation.Id, installation.CustomerId, installation.SupplyPointId, "24ZZS12345678901", 8m, 10.8m, null,
                [
                    new AttachmentInput(ConnectionRequestAttachmentKind.SingleLineDiagram, "schema.pdf", "application/pdf", 100),
                    new AttachmentInput(ConnectionRequestAttachmentKind.OwnerConsent, "suhlas.pdf", "application/pdf", 100),
                ],
                Guid.CreateVersion7(),
                now);
            if (state != ConnectionRequestState.Draft)
            {
                request.Submit(DateOnly.FromDateTime(now.UtcDateTime).AddDays(10), now);
            }

            if (state == ConnectionRequestState.Rejected)
            {
                request.Reject("Nedostatočná kapacita siete", now);
            }
            else if (state != ConnectionRequestState.Draft && state != ConnectionRequestState.Submitted)
            {
                request.OfferContract(null, now);
            }

            if (state == ConnectionRequestState.ContractSigned)
            {
                request.ReviewConditions(now);
                request.SendContract(now);
                request.CustomerSigned(now);
            }

            db.Add(request);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Walks the installation to <paramref name="target"/> through the domain model (see <see cref="InstallationLifecycle"/>).</summary>
    public static async Task WalkToAsync(this ApiFactory factory, Guid installationId, InstallationState target, DateTimeOffset now)
    {
        await factory.ExecuteDbAsync(async db =>
        {
            var installation = await db.Set<Installation>()
                .Include(x => x.StateChanges)
                .Include(x => x.ChecklistItems)
                .FirstAsync(x => x.Id == installationId);
            db.AddRange(InstallationLifecycle.WalkTo(installation, target, now));
            await db.SaveChangesAsync();
        });
    }
}

/// <summary><see cref="ApiFactory"/> with a fixed clock, so date rules ("not in the past") are deterministic.</summary>
internal sealed class ClockedApiFactory(PostgresFixture postgres, TimeProvider clock) : ApiFactory(postgres)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton(clock));
    }
}
