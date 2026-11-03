using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using InstallationData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>API host with a fixed clock for the ticket tests.</summary>
internal sealed class TicketsFactory(PostgresFixture postgres, TimeProvider clock) : ApiFactory(postgres)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton(clock));
    }
}

/// <summary>Two customers with accounts, each with one installation.</summary>
internal sealed record TicketSeed(
    Guid UserA,
    Guid UserB,
    CustomerResponse CustomerA,
    CustomerResponse CustomerB,
    InstallationResponse InstallationA,
    InstallationResponse InstallationB);

internal static class TicketTestApi
{
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    public static CreateTicketRequest NewTicket(Guid installationId, ServiceTicketPriority? priority = null) => new()
    {
        InstallationId = installationId,
        Title = "Invertor hlási chybu",
        Description = "Od rána FVE nevyrába, na invertore svieti červená LED.",
        Priority = priority,
    };

    public static TicketAttachmentRequest Photo(string contentType = "image/jpeg", byte[]? content = null) => new()
    {
        FileName = "porucha.jpg",
        ContentType = contentType,
        ContentBase64 = Convert.ToBase64String(content ?? JpegBytes),
    };

    public static async Task<TicketSeed> SeedAsync(ApiFactory factory)
    {
        var userA = await NewUserAsync(factory);
        var userB = await NewUserAsync(factory);
        using var sales = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person($"a-{Guid.NewGuid():N}@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(
            sales, CustomerTestData.Person($"b-{Guid.NewGuid():N}@example.sk", lastName: "Kováč") with { UserId = userB });

        using var op = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var installationA = (await InstallationData.CreateInstallationAsync(op, InstallationData.NewInstallationRequest() with { CustomerId = customerA.Id })).Installation;
        var installationB = (await InstallationData.CreateInstallationAsync(op, InstallationData.NewInstallationRequest() with { CustomerId = customerB.Id })).Installation;
        return new TicketSeed(userA, userB, customerA, customerB, installationA, installationB);
    }

    public static async Task<TicketResponse> CreateTicketAsync(this HttpClient client, CreateTicketRequest request)
    {
        using var response = await client.PostJsonAsync("/api/tickets", request);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<TicketResponse>();
    }

    public static async Task<TicketResponse> TransitionAsync(this HttpClient client, Guid id, ServiceTicketState state, string? note = null)
    {
        using var response = await client.PostJsonAsync($"/api/tickets/{id}/transition", new TransitionTicketRequest { State = state, Note = note });
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<TicketResponse>();
    }

    private static async Task<Guid> NewUserAsync(ApiFactory factory)
    {
        var user = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
        await factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user.Id;
    }
}
