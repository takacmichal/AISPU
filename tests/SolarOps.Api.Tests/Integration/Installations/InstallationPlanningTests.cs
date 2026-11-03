using System.Net;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Installations;

/// <summary>Planning rules: approved connection request and no past date (#88), technician collisions and list (#89).</summary>
[Collection(PostgresCollection.Name)]
public sealed class InstallationPlanningTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);

    private readonly ApiFactory _factory = new ClockedApiFactory(postgres, new FixedTimeProvider(Now));

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, Guid id, DateOnly start, DateOnly end) =>
        client.PutJsonAsync($"/api/installations/{id}/schedule", new ScheduleInstallationRequest { ScheduledStart = start, ScheduledEnd = end });

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid id, params Guid[] technicians) =>
        client.PutJsonAsync($"/api/installations/{id}/technicians", new AssignTechniciansRequest { TechnicianIds = technicians });

    private async Task<InstallationResponse> ApprovedInstallationAsync(HttpClient client)
    {
        var installation = (await client.CreateInstallationAsync()).Installation;
        await _factory.SeedConnectionRequestAsync(installation);
        return installation;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ConnectionRequestState.Draft)]
    [InlineData(ConnectionRequestState.Submitted)]
    [InlineData(ConnectionRequestState.Rejected)]
    public async Task Schedule_WithoutApprovedConnectionRequest_Returns409(ConnectionRequestState? state)
    {
        using var client = Operator();
        var installation = (await client.CreateInstallationAsync()).Installation;
        if (state is { } s)
        {
            await _factory.SeedConnectionRequestAsync(installation, s);
        }

        using var response = await ScheduleAsync(client, installation.Id, Today.AddDays(1), Today.AddDays(2));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("connection request", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ConnectionRequestState.ContractOffered)]
    [InlineData(ConnectionRequestState.ContractSigned)]
    public async Task Schedule_AfterApproval_Returns200(ConnectionRequestState state)
    {
        using var client = Operator();
        var installation = (await client.CreateInstallationAsync()).Installation;
        await _factory.SeedConnectionRequestAsync(installation, ConnectionRequestState.Rejected);
        await _factory.SeedConnectionRequestAsync(installation, state);

        using var response = await ScheduleAsync(client, installation.Id, Today, Today.AddDays(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<InstallationDetailResponse>();
        Assert.Equal(Today, body!.Installation.ScheduledStart);
    }

    [Fact]
    public async Task Schedule_InThePast_Returns400()
    {
        using var client = Operator();
        var installation = await ApprovedInstallationAsync(client);

        using var pastStart = await ScheduleAsync(client, installation.Id, Today.AddDays(-1), Today.AddDays(1));
        using var pastBoth = await ScheduleAsync(client, installation.Id, Today.AddDays(-3), Today.AddDays(-2));

        Assert.Equal(HttpStatusCode.BadRequest, pastStart.StatusCode);
        Assert.Contains("ScheduledStart", await pastStart.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, pastBoth.StatusCode);
    }

    [Fact]
    public async Task AssignTechnicians_OverlappingWindowOfAnotherInstallation_Returns409()
    {
        using var client = Operator();
        var technician = await _factory.SeedTechnicianAsync();
        var other = await _factory.SeedTechnicianAsync();
        var first = await ApprovedInstallationAsync(client);
        var second = await ApprovedInstallationAsync(client);
        (await ScheduleAsync(client, first.Id, Today.AddDays(1), Today.AddDays(3))).EnsureSuccessStatusCode();
        (await AssignAsync(client, first.Id, technician)).EnsureSuccessStatusCode();
        (await ScheduleAsync(client, second.Id, Today.AddDays(3), Today.AddDays(4))).EnsureSuccessStatusCode();

        using var collision = await AssignAsync(client, second.Id, other, technician);
        using var free = await AssignAsync(client, second.Id, other);

        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        var problem = await collision.Content.ReadAsStringAsync();
        Assert.Contains("collision", problem);
        Assert.Contains(technician.ToString(), problem);
        Assert.Equal(HttpStatusCode.OK, free.StatusCode);
    }

    [Fact]
    public async Task Schedule_MovingIntoAnotherWindowOfTheSameTechnician_Returns409_AdjacentDaysAreFine()
    {
        using var client = Operator();
        var technician = await _factory.SeedTechnicianAsync();
        var first = await ApprovedInstallationAsync(client);
        var second = await ApprovedInstallationAsync(client);
        (await ScheduleAsync(client, first.Id, Today.AddDays(1), Today.AddDays(2))).EnsureSuccessStatusCode();
        (await AssignAsync(client, first.Id, technician)).EnsureSuccessStatusCode();
        (await ScheduleAsync(client, second.Id, Today.AddDays(5), Today.AddDays(6))).EnsureSuccessStatusCode();
        (await AssignAsync(client, second.Id, technician)).EnsureSuccessStatusCode();

        using var overlapping = await ScheduleAsync(client, second.Id, Today.AddDays(2), Today.AddDays(3));
        using var adjacent = await ScheduleAsync(client, second.Id, Today.AddDays(3), Today.AddDays(4));

        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adjacent.StatusCode);
    }

    [Fact]
    public async Task Collision_IgnoresCommissionedInstallations()
    {
        using var client = Operator();
        var technician = await _factory.SeedTechnicianAsync();
        var done = await ApprovedInstallationAsync(client);
        (await ScheduleAsync(client, done.Id, Today, Today.AddDays(2))).EnsureSuccessStatusCode();
        (await AssignAsync(client, done.Id, technician)).EnsureSuccessStatusCode();
        await _factory.WalkToAsync(done.Id, InstallationState.Commissioned, Now);
        var next = await ApprovedInstallationAsync(client);
        (await ScheduleAsync(client, next.Id, Today.AddDays(1), Today.AddDays(2))).EnsureSuccessStatusCode();

        using var response = await AssignAsync(client, next.Id, technician);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Technicians_ListsUsersWithRoleTechnician_ForPlanners()
    {
        var technician = await _factory.SeedTechnicianAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

        using var response = await sales.GetAsync("/api/installations/technicians");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.ReadAsync<List<TechnicianResponse>>();
        var item = Assert.Single(list!);
        Assert.Equal(technician, item.Id);
        Assert.Equal("tech@example.com", item.Email);
    }

    [Fact]
    public async Task Technicians_Returns401Anonymous_And403ForTechnicianAndCustomer()
    {
        using var anonymous = _factory.CreateApiClient();
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/installations/technicians")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.GetAsync("/api/installations/technicians")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/installations/technicians")).StatusCode);
    }
}
