using System.Net;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Notifications;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Tickets;

namespace SolarOps.Api.Tests.Integration.Notifications;

/// <summary><c>GET /api/notifications</c>: recent changes of tickets and connection requests the user may see.</summary>
public sealed class NotificationEndpointsTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/notifications";

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));
    private readonly TicketsFactory _factory;

    public NotificationEndpointsTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private static async Task<IReadOnlyList<NotificationItemResponse>> ItemsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync<NotificationListResponse>()).Items;
    }

    private async Task<ConnectionRequestResponse> CreateConnectionRequestDraftAsync(Guid installationId)
    {
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var request = new CreateConnectionRequestRequest
        {
            InstallationId = installationId,
            EicCode = "24ZZS12345678901",
            RatedAcPowerKw = 8m,
            MainBreakerAmps = 25,
            Attachments =
            [
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.SingleLineDiagram, FileName = "schema.pdf", ContentType = "application/pdf", SizeBytes = 100 },
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.OwnerConsent, FileName = "suhlas.pdf", ContentType = "application/pdf", SizeBytes = 100 },
            ],
        };
        using var response = await sales.PostJsonAsync("/api/connection-requests", request);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<ConnectionRequestResponse>();
    }

    private async Task SubmitConnectionRequestAsync(Guid id)
    {
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var response = await designer.PostAsync($"/api/connection-requests/{id}/submit", null);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Anonymous_Gets401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_SeesChangesOfOwnTicket_NewestFirst_AndNotForeignTickets()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = Operator();
        var own = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationB.Id));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await op.TransitionAsync(own.Id, ServiceTicketState.Triaged);
        using var customer = _factory.CreateApiClient(seed.UserA, Roles.Customer);

        var items = await ItemsAsync(customer);

        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(NotificationKind.Ticket, item.Kind);
            Assert.Equal($"/tickets/{own.Id}", item.Link);
        });
        Assert.Equal("Invertor hlási chybu: Zatriedený", items[0].Title);
        Assert.Equal("Invertor hlási chybu: Nový", items[1].Title);
        Assert.True(items[0].CreatedAt > items[1].CreatedAt);
        Assert.NotEqual(items[0].Id, items[1].Id);
    }

    [Fact]
    public async Task OwnChanges_AreNotReported()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = Operator();

        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        Assert.Empty(await ItemsAsync(op));
    }

    [Fact]
    public async Task Technician_SeesOnlyAssignedTickets_AndNoConnectionRequests()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var technicianId = Guid.CreateVersion7();
        using var op = Operator();
        var assigned = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationB.Id));
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Add(ServiceTicketAssignment.Create(assigned.Id, technicianId, Guid.CreateVersion7(), _clock.GetUtcNow()));
            await db.SaveChangesAsync();
        });
        var draft = await CreateConnectionRequestDraftAsync(seed.InstallationA.Id);
        await SubmitConnectionRequestAsync(draft.Id);
        using var technician = _factory.CreateApiClient(technicianId, Roles.Technician);

        var items = await ItemsAsync(technician);

        var item = Assert.Single(items);
        Assert.Equal($"/tickets/{assigned.Id}", item.Link);
    }

    [Fact]
    public async Task ConnectionRequest_IsReportedAfterSubmission_OnlyToThoseWhoMaySeeIt()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var draft = await CreateConnectionRequestDraftAsync(seed.InstallationA.Id);
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var owner = _factory.CreateApiClient(seed.UserA, Roles.Customer);
        using var foreign = _factory.CreateApiClient(seed.UserB, Roles.Customer);

        Assert.Empty(await ItemsAsync(owner));

        await SubmitConnectionRequestAsync(draft.Id);

        foreach (var client in new[] { sales, owner })
        {
            var item = Assert.Single(await ItemsAsync(client));
            Assert.Equal(NotificationKind.ConnectionRequest, item.Kind);
            Assert.Equal("Žiadosť o pripojenie 24ZZS12345678901: Podaná distribučke", item.Title);
            Assert.Equal($"/connection-requests/{draft.Id}", item.Link);
        }

        Assert.Empty(await ItemsAsync(foreign));
    }

    [Fact]
    public async Task ChangesOlderThanTheWindow_AreLeftOut()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = Operator();
        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        using var customer = _factory.CreateApiClient(seed.UserA, Roles.Customer);
        Assert.Single(await ItemsAsync(customer));

        _clock.Advance(TimeSpan.FromDays(NotificationService.WindowDays + 1));

        Assert.Empty(await ItemsAsync(customer));
    }

    [Fact]
    public async Task List_IsLimited_ToTheNewestItems()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = Operator();
        for (var i = 0; i <= NotificationService.MaxItems; i++)
        {
            await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        using var customer = _factory.CreateApiClient(seed.UserA, Roles.Customer);

        var items = await ItemsAsync(customer);

        Assert.Equal(NotificationService.MaxItems, items.Count);
        Assert.Equal(items.OrderByDescending(x => x.CreatedAt).Select(x => x.Id), items.Select(x => x.Id));
    }
}
