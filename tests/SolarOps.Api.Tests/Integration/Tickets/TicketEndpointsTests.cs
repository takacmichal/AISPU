using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>Creating and reading tickets (#120), authorization and the customer scope for every endpoint.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TicketEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private readonly TicketsFactory _factory;

    public TicketEndpointsTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, new FixedTimeProvider(Now));

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient As(string role, Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), role);

    [Fact]
    public async Task Create_ByOperator_ReturnsCreatedManualTicketInStateNew()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var operatorId = Guid.CreateVersion7();
        using var client = As(Roles.Operator, operatorId);

        using var response = await client.PostJsonAsync("/api/tickets", TicketTestApi.NewTicket(seed.InstallationA.Id, ServiceTicketPriority.High));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.ReadJsonAsync<TicketResponse>();
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(ServiceTicketState.New, ticket.State);
        Assert.Equal(ServiceTicketOrigin.Manual, ticket.Origin);
        Assert.Equal(ServiceTicketPriority.High, ticket.Priority);
        Assert.Equal(seed.InstallationA.Id, ticket.InstallationId);
        Assert.Equal(seed.CustomerA.Id, ticket.CustomerId);
        Assert.Equal(operatorId, ticket.CreatedBy);
        Assert.Equal(Now, ticket.CreatedAt);
        Assert.Equal([ServiceTicketState.Triaged], ticket.AllowedTransitions);
    }

    [Fact]
    public async Task Create_ByCustomer_ForOwnInstallation_IsCustomerReportWithDefaultPriority()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var client = As(Roles.Customer, seed.UserA);

        using var response = await client.PostJsonAsync(
            "/api/tickets",
            TicketTestApi.NewTicket(seed.InstallationA.Id, ServiceTicketPriority.High) with { Attachments = [TicketTestApi.Photo()] });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.ReadJsonAsync<TicketResponse>();
        Assert.Equal(ServiceTicketOrigin.CustomerReport, ticket.Origin);
        Assert.Equal(ServiceTicketPriority.Medium, ticket.Priority);
        Assert.Equal(seed.CustomerA.Id, ticket.CustomerId);
        var attachment = Assert.Single(ticket.Attachments);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(TicketTestApi.JpegBytes.Length, attachment.SizeBytes);
    }

    [Fact]
    public async Task Create_ByCustomer_ForForeignInstallation_AnswersLikeAnUnknownInstallation()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var client = As(Roles.Customer, seed.UserA);

        using var foreign = await client.PostJsonAsync("/api/tickets", TicketTestApi.NewTicket(seed.InstallationB.Id));
        using var missing = await client.PostJsonAsync("/api/tickets", TicketTestApi.NewTicket(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        // Same validation errors; only per-request fields such as traceId differ.
        var foreignErrors = (await foreign.ReadJsonAsync<ValidationProblemDetails>()).Errors;
        var missingErrors = (await missing.ReadJsonAsync<ValidationProblemDetails>()).Errors;
        Assert.Equal(missingErrors.Keys, foreignErrors.Keys);
        Assert.Equal(missingErrors.Values.SelectMany(v => v), foreignErrors.Values.SelectMany(v => v));
        Assert.Contains(nameof(CreateTicketRequest.InstallationId), foreignErrors.Keys);
        Assert.Equal(0, await _factory.ExecuteDbAsync(db => Task.FromResult(db.Set<ServiceTicket>().Count())));
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var client = As(Roles.Operator);
        var valid = TicketTestApi.NewTicket(seed.InstallationA.Id);

        using var noTitle = await client.PostJsonAsync("/api/tickets", valid with { Title = " " });
        using var noInstallation = await client.PostJsonAsync("/api/tickets", valid with { InstallationId = null });
        using var gif = await client.PostJsonAsync("/api/tickets", valid with { Attachments = [TicketTestApi.Photo("image/gif")] });
        using var badBase64 = await client.PostJsonAsync("/api/tickets",
            valid with { Attachments = [TicketTestApi.Photo() with { ContentBase64 = "not-base64!" }] });
        using var tooMany = await client.PostJsonAsync("/api/tickets",
            valid with { Attachments = Enumerable.Repeat(TicketTestApi.Photo(), ServiceTicket.MaxAttachments + 1).ToList() });

        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noInstallation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gif.StatusCode);
        Assert.Contains("Attachments[0].ContentType", await gif.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badBase64.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Create_AttachmentOverTheSizeLimit_Returns400()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var client = As(Roles.Operator);
        var oversized = new byte[ServiceTicket.MaxAttachmentBytes + 1];

        using var response = await client.PostJsonAsync("/api/tickets",
            TicketTestApi.NewTicket(seed.InstallationA.Id) with { Attachments = [TicketTestApi.Photo(content: oversized)] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsDetail_AndAttachmentCanBeDownloaded()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var technician = As(Roles.Technician);
        var created = await technician.CreateTicketAsync(
            TicketTestApi.NewTicket(seed.InstallationA.Id) with { Attachments = [TicketTestApi.Photo()] });

        var detail = await technician.GetJsonAsync<TicketResponse>($"/api/tickets/{created.Id}");
        var attachment = Assert.Single(detail.Attachments);
        using var download = await technician.GetAsync(attachment.DownloadUrl);

        Assert.Equal(created.Id, detail.Id);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(TicketTestApi.JpegBytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnknownTicket_Returns404_OnEveryTicketEndpoint()
    {
        using var client = As(Roles.Admin);
        var id = Guid.CreateVersion7();

        using var get = await client.GetAsync($"/api/tickets/{id}");
        using var history = await client.GetAsync($"/api/tickets/{id}/history");
        using var comments = await client.GetAsync($"/api/tickets/{id}/comments");
        using var attachment = await client.GetAsync($"/api/tickets/{id}/attachments/{Guid.CreateVersion7()}");
        using var triage = await client.PutJsonAsync($"/api/tickets/{id}/triage", new TriageTicketRequest { Priority = ServiceTicketPriority.Low });
        using var transition = await client.PostJsonAsync($"/api/tickets/{id}/transition", new TransitionTicketRequest { State = ServiceTicketState.Triaged });
        using var comment = await client.PostJsonAsync($"/api/tickets/{id}/comments", new AddTicketCommentRequest { Body = "x" });

        Assert.All(
            new[] { get, history, comments, attachment, triage, transition, comment },
            r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "POST", "/api/tickets" },
        { "GET", "/api/tickets" },
        { "GET", "/api/tickets/{id}" },
        { "PUT", "/api/tickets/{id}/triage" },
        { "POST", "/api/tickets/{id}/transition" },
        { "GET", "/api/tickets/{id}/history" },
        { "POST", "/api/tickets/{id}/comments" },
        { "GET", "/api/tickets/{id}/comments" },
        { "GET", "/api/tickets/{id}/attachments/{attachmentId}" },
    };

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Anonymous_Returns401(string method, string route)
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.SendAsync(Request(method, route));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/tickets", Roles.Sales)]
    [InlineData("POST", "/api/tickets", Roles.Designer)]
    [InlineData("PUT", "/api/tickets/{id}/triage", Roles.Technician)]
    [InlineData("PUT", "/api/tickets/{id}/triage", Roles.Customer)]
    [InlineData("POST", "/api/tickets/{id}/transition", Roles.Technician)]
    [InlineData("POST", "/api/tickets/{id}/transition", Roles.Customer)]
    [InlineData("POST", "/api/tickets/{id}/transition", Roles.Sales)]
    [InlineData("POST", "/api/tickets/{id}/comments", Roles.Customer)]
    [InlineData("POST", "/api/tickets/{id}/comments", Roles.Sales)]
    public async Task WrongRole_Returns403(string method, string route, string role)
    {
        using var client = As(role);

        using var response = await client.SendAsync(Request(method, route));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_GetsForeignTicket_As404_OnEveryReadEndpoint_AndSeesOnlyOwnInList()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var own = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        var foreign = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationB.Id) with { Attachments = [TicketTestApi.Photo()] });
        using var customer = As(Roles.Customer, seed.UserA);

        using var get = await customer.GetAsync($"/api/tickets/{foreign.Id}");
        using var history = await customer.GetAsync($"/api/tickets/{foreign.Id}/history");
        using var comments = await customer.GetAsync($"/api/tickets/{foreign.Id}/comments");
        using var attachment = await customer.GetAsync(foreign.Attachments[0].DownloadUrl);
        var list = await customer.GetJsonAsync<TicketListResponse>("/api/tickets");
        var byForeignInstallation = await customer.GetJsonAsync<TicketListResponse>($"/api/tickets?installationId={seed.InstallationB.Id}");

        Assert.All(new[] { get, history, comments, attachment }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal([own.Id], list.Items.Select(t => t.Id));
        Assert.Equal(1, list.TotalCount);
        Assert.Empty(byForeignInstallation.Items);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/tickets/{own.Id}")).StatusCode);
    }

    [Fact]
    public async Task Customer_WithoutLinkedCustomer_SeesNothing()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        using var stranger = As(Roles.Customer);

        var list = await stranger.GetJsonAsync<TicketListResponse>("/api/tickets");
        using var get = await stranger.GetAsync($"/api/tickets/{ticket.Id}");
        using var create = await stranger.PostJsonAsync("/api/tickets", TicketTestApi.NewTicket(seed.InstallationA.Id));

        Assert.Empty(list.Items);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    private static HttpRequestMessage Request(string method, string route)
    {
        var url = route.Replace("{id}", Guid.CreateVersion7().ToString()).Replace("{attachmentId}", Guid.CreateVersion7().ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }
}
