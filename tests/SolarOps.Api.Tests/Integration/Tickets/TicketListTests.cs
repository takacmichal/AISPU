using System.Net;
using Npgsql;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>Filtering, sorting and paging of tickets (#122).</summary>
[Collection(PostgresCollection.Name)]
public sealed class TicketListTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private readonly FixedTimeProvider _clock = new(Start);
    private readonly TicketsFactory _factory;

    public TicketListTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private sealed record Tickets(
        TicketSeed Seed,
        TicketResponse ManualHighA,
        TicketResponse ManualLowA,
        TicketResponse CustomerA,
        TicketResponse ManualMediumB);

    /// <summary>Four tickets one minute apart: three on installation A (one reported by the customer), one on B.</summary>
    private async Task<Tickets> SeedAsync()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        using var customer = _factory.CreateApiClient(seed.UserA, Roles.Customer);

        var high = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id, ServiceTicketPriority.High));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var low = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id, ServiceTicketPriority.Low));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var reported = await customer.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var medium = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationB.Id, ServiceTicketPriority.Medium));
        await op.TransitionAsync(high.Id, ServiceTicketState.Triaged);

        return new Tickets(seed, high, low, reported, medium);
    }

    private async Task<TicketListResponse> ListAsync(string query)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        return await client.GetJsonAsync<TicketListResponse>($"/api/tickets{query}");
    }

    [Fact]
    public async Task Default_IsNewestFirst_WithFirstPage()
    {
        var t = await SeedAsync();

        var page = await ListAsync(string.Empty);

        Assert.Equal([t.ManualMediumB.Id, t.CustomerA.Id, t.ManualLowA.Id, t.ManualHighA.Id], page.Items.Select(i => i.Id));
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Origin_SeparatesCustomerReportsFromManualTickets()
    {
        var t = await SeedAsync();

        var reports = await ListAsync("?origin=CustomerReport");
        var manual = await ListAsync("?origin=Manual");

        Assert.Equal([t.CustomerA.Id], reports.Items.Select(i => i.Id));
        Assert.Equal(3, manual.TotalCount);
        Assert.All(manual.Items, i => Assert.Equal(ServiceTicketOrigin.Manual, i.Origin));
    }

    [Fact]
    public async Task Filters_AreCombinable()
    {
        var t = await SeedAsync();

        var newOnA = await ListAsync($"?installationId={t.Seed.InstallationA.Id}&state=New");
        var newManualLowOnA = await ListAsync($"?installationId={t.Seed.InstallationA.Id}&state=New&origin=Manual&priority=Low");
        var triagedHigh = await ListAsync("?state=Triaged&priority=High");
        var nothing = await ListAsync($"?installationId={t.Seed.InstallationB.Id}&priority=High");

        Assert.Equal([t.CustomerA.Id, t.ManualLowA.Id], newOnA.Items.Select(i => i.Id));
        Assert.Equal([t.ManualLowA.Id], newManualLowOnA.Items.Select(i => i.Id));
        Assert.Equal([t.ManualHighA.Id], triagedHigh.Items.Select(i => i.Id));
        Assert.Empty(nothing.Items);
        Assert.Equal(0, nothing.TotalCount);
    }

    [Fact]
    public async Task Paging_SplitsTheResult_AndKeepsTheTotal()
    {
        var t = await SeedAsync();

        var first = await ListAsync("?page=1&pageSize=3");
        var second = await ListAsync("?page=2&pageSize=3");

        Assert.Equal(3, first.Items.Count);
        Assert.Equal([t.ManualHighA.Id], second.Items.Select(i => i.Id));
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(2, second.Page);
    }

    [Theory]
    [InlineData(1073741825)]
    [InlineData(int.MaxValue)]
    public async Task Paging_LargePage_ReturnsEmptyWithTotal(int pageNumber)
    {
        await SeedAsync();

        var page = await ListAsync($"?page={pageNumber}&pageSize=20");

        Assert.Empty(page.Items);
        Assert.Equal(pageNumber, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task SortByPriority_RanksHighMediumLow_NotAlphabetically()
    {
        var t = await SeedAsync();

        var desc = await ListAsync("?sortBy=priority&sortDir=desc");
        var asc = await ListAsync("?sortBy=priority&sortDir=asc");

        Assert.Equal(
            [ServiceTicketPriority.High, ServiceTicketPriority.Medium, ServiceTicketPriority.Medium, ServiceTicketPriority.Low],
            desc.Items.Select(i => i.Priority));
        Assert.Equal(t.ManualLowA.Id, asc.Items[0].Id);
        Assert.Equal(t.ManualHighA.Id, asc.Items[^1].Id);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?sortBy=title")]
    [InlineData("?sortDir=up")]
    [InlineData("?state=Teleported")]
    public async Task InvalidQuery_Returns400(string query)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

        using var response = await client.GetAsync($"/api/tickets{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
