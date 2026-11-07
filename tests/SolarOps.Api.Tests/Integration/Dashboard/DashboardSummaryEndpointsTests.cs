using System.Net;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Dashboard;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Tickets;

namespace SolarOps.Api.Tests.Integration.Dashboard;

/// <summary>
/// <c>GET /api/dashboard/summary</c>. Tickets: 2 New (High, Medium), 1 Triaged (Low), 1 Resolved (High), 1 Closed (Medium)
/// -> open 3, new 2, byPriority high 1, medium 1, low 1. Quotes: 1 Draft, 2 Sent (gross 123 and 246), 1 Accepted
/// -> sent 2, sentGrossTotal 369, open 3.
/// </summary>
public sealed class DashboardSummaryEndpointsTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/dashboard/summary";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly TicketsFactory _factory;

    public DashboardSummaryEndpointsTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, new FixedTimeProvider(Now));

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private async Task SeedAsync()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var installation = seed.InstallationA.Id;

        await op.CreateTicketAsync(TicketTestApi.NewTicket(installation, ServiceTicketPriority.High));
        await op.CreateTicketAsync(TicketTestApi.NewTicket(installation, ServiceTicketPriority.Medium));

        var low = await op.CreateTicketAsync(TicketTestApi.NewTicket(installation, ServiceTicketPriority.Low));
        await op.TransitionAsync(low.Id, ServiceTicketState.Triaged);

        var resolved = await op.CreateTicketAsync(TicketTestApi.NewTicket(installation, ServiceTicketPriority.High));
        await op.TransitionAsync(resolved.Id, ServiceTicketState.Triaged);
        await op.TransitionAsync(resolved.Id, ServiceTicketState.RemoteDiagnosis);
        await op.TransitionAsync(resolved.Id, ServiceTicketState.Resolved);

        var closed = await op.CreateTicketAsync(TicketTestApi.NewTicket(installation, ServiceTicketPriority.Medium));
        await op.TransitionAsync(closed.Id, ServiceTicketState.Triaged);
        await op.TransitionAsync(closed.Id, ServiceTicketState.RemoteDiagnosis);
        await op.TransitionAsync(closed.Id, ServiceTicketState.Resolved);
        await op.TransitionAsync(closed.Id, ServiceTicketState.Closed);

        var by = Guid.CreateVersion7();
        await _factory.ExecuteDbAsync(async db =>
        {
            Quote NewQuote(decimal quantity) => Quote.Create(
                seed.CustomerA.Id,
                [new QuoteLineData(Guid.CreateVersion7(), ComponentType.Panel, "PAN-1", "Panel", 100m, quantity)],
                23m,
                null,
                by,
                Now);

            var sentOne = NewQuote(1);
            sentOne.Send(30, by, null, Now);
            var sentTwo = NewQuote(2);
            sentTwo.Send(30, by, null, Now);
            var accepted = NewQuote(1);
            accepted.Send(30, by, null, Now);
            accepted.Accept(by, null, Now);
            db.AddRange(sentOne, sentTwo, accepted, NewQuote(1));
            await db.SaveChangesAsync();
        });
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Sales)]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Operator)]
    public async Task QuotesRoles_SeeTicketsAndQuotes(string role)
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        var result = await client.GetJsonAsync<DashboardSummaryResponse>(Url);

        Assert.Equal(Now, result.GeneratedAt);
        Assert.NotNull(result.Tickets);
        Assert.Equal(3, result.Tickets.Open);
        Assert.Equal(2, result.Tickets.New);
        Assert.Equal(1, result.Tickets.ByPriority.High);
        Assert.Equal(1, result.Tickets.ByPriority.Medium);
        Assert.Equal(1, result.Tickets.ByPriority.Low);
        Assert.NotNull(result.Quotes);
        Assert.Equal(2, result.Quotes.Sent);
        Assert.Equal(369m, result.Quotes.SentGrossTotal);
        Assert.Equal(3, result.Quotes.Open);
    }

    [Fact]
    public async Task Technician_SeesTickets_ButNoQuotes()
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);

        var result = await client.GetJsonAsync<DashboardSummaryResponse>(Url);

        Assert.NotNull(result.Tickets);
        Assert.Equal(3, result.Tickets.Open);
        Assert.Null(result.Quotes);
    }

    [Fact]
    public async Task EmptyDatabase_GivesZeroes()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

        var result = await client.GetJsonAsync<DashboardSummaryResponse>(Url);

        Assert.Equal(0, result.Tickets!.Open);
        Assert.Equal(0, result.Tickets.ByPriority.High);
        Assert.Equal(0, result.Quotes!.Sent);
        Assert.Equal(0m, result.Quotes.SentGrossTotal);
    }

    [Fact]
    public async Task Customer_Returns403()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
