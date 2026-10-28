using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using static SolarOps.Api.Tests.Integration.FveDesigns.FveDesignTestApi;

namespace SolarOps.Api.Tests.Integration.FveDesigns;

/// <summary>Approval, rework and quote creation from a design (#78).</summary>
[Collection(PostgresCollection.Name)]
public sealed class FveDesignApprovalTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);
    private readonly Guid _salesId = Guid.CreateVersion7();

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(_salesId, Roles.Sales);

    private HttpClient Designer() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);

    private HttpClient Admin() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

    private sealed record Setup(CustomerResponse Customer, ComponentResponse Panel, ComponentResponse Inverter);

    private async Task<Setup> SetupAsync()
    {
        using var admin = Admin();
        using var sales = Sales();
        return new Setup(
            await CustomerTestData.CreateAsync(sales, CustomerTestData.Person()),
            await CreateComponentAsync(admin, ComponentType.Panel, "PAN-450", 450, 120.5m),
            await CreateComponentAsync(admin, ComponentType.Inverter, "INV-5K", 5000, 950m));
    }

    private static async Task<ComponentResponse> CreateComponentAsync(HttpClient admin, ComponentType type, string code, int power, decimal price)
    {
        using var response = await admin.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = type,
            Code = code,
            Name = $"Komponent {code}",
            PowerW = power,
            UnitPrice = price,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ComponentResponse>();
    }

    private static ConfigureDesignRequest Config(Setup s, int count = 12) =>
        new() { PanelId = s.Panel.Id, PanelCount = count, InverterId = s.Inverter.Id };

    /// <summary>A design for the customer that is InDesign with one saved version.</summary>
    private async Task<FveDesignResponse> InDesignWithVersionAsync(Setup s, int panelCount = 12)
    {
        using var sales = Sales();
        using var designer = Designer();
        var design = await (await sales.PostAsJsonAsync(
                "/api/designs", CreateRequest() with { CustomerId = s.Customer.Id }, Json))
            .ReadAsync<FveDesignResponse>(HttpStatusCode.Created);
        await designer.SurveyAsync(design.Id);
        await designer.SetParametersAsync(design.Id);
        await SaveVersionAsync(designer, design.Id, Config(s, panelCount));
        return design;
    }

    private static async Task SaveVersionAsync(HttpClient designer, Guid designId, ConfigureDesignRequest config) =>
        Assert.Equal(HttpStatusCode.Created, (await designer.PostJsonAsync($"/api/designs/{designId}/versions", config)).StatusCode);

    private async Task<FveDesignResponse> ApprovedDesignAsync(Setup s, int panelCount = 12)
    {
        var design = await InDesignWithVersionAsync(s, panelCount);
        using var sales = Sales();
        return await (await sales.PostAsync($"/api/designs/{design.Id}/approve", null)).ReadAsync<FveDesignResponse>(HttpStatusCode.OK);
    }

    private static async Task<QuoteResponse> QuoteFromDesignAsync(HttpClient client, Guid designId) =>
        await (await client.PostAsync($"/api/designs/{designId}/quote", null)).ReadAsync<QuoteResponse>(HttpStatusCode.Created);

    [Fact]
    public async Task Approve_InDesignWithVersion_ApprovesLatestVersionAndShowsApprover()
    {
        var s = await SetupAsync();
        var design = await InDesignWithVersionAsync(s);
        using var designer = Designer();
        await SaveVersionAsync(designer, design.Id, Config(s, 14));
        using var sales = Sales();

        var approved = await (await sales.PostAsync($"/api/designs/{design.Id}/approve", null)).ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

        Assert.Equal(FveDesignStatus.Approved, approved.Status);
        Assert.NotNull(approved.Approval);
        Assert.Equal(_salesId, approved.Approval.ApprovedBy);
        Assert.Equal(2, approved.Approval.VersionNumber);

        var fetched = await sales.GetJsonAsync<FveDesignResponse>($"/api/designs/{design.Id}");
        Assert.Equal(_salesId, fetched.Approval?.ApprovedBy);
    }

    [Fact]
    public async Task Approve_WithoutSavedVersion_Returns409()
    {
        using var sales = Sales();
        using var designer = Designer();
        var design = await sales.CreateDesignAsync();
        await designer.SurveyAsync(design.Id);
        await designer.SetParametersAsync(design.Id);

        var response = await sales.PostAsync($"/api/designs/{design.Id}/approve", null);

        await response.ReadAsync<ProblemDetails>(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Approve_Twice_Returns409()
    {
        var design = await ApprovedDesignAsync(await SetupAsync());
        using var sales = Sales();

        var response = await sales.PostAsync($"/api/designs/{design.Id}/approve", null);

        await response.ReadAsync<ProblemDetails>(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Approve_UnknownDesign_Returns404()
    {
        using var sales = Sales();

        var response = await sales.PostAsync($"/api/designs/{Guid.CreateVersion7()}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsync($"/api/designs/{Guid.CreateVersion7()}/approve", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Customer)]
    public async Task ApproveReworkAndCreateQuote_WithWrongRole_Return403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/designs/{id}/approve", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/designs/{id}/rework", new ReworkDesignRequest { Reason = "x" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/designs/{id}/quote", null)).StatusCode);
    }

    [Fact]
    public async Task Approved_DesignCannotBeChangedUntilReworked()
    {
        var s = await SetupAsync();
        var design = await ApprovedDesignAsync(s);
        using var designer = Designer();

        var save = await designer.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(s, 10));
        var update = await designer.PutAsJsonAsync($"/api/designs/{design.Id}/parameters", Parameters(), Json);

        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_FromApprovedDesign_BuildsDraftWithLinkAndSnapshotPrices()
    {
        var s = await SetupAsync();
        var design = await ApprovedDesignAsync(s, panelCount: 12);
        using var admin = Admin();
        using var catalogUpdate = await admin.PutJsonAsync($"/api/components/{s.Panel.Id}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = s.Panel.Code,
            Name = "Nový názov",
            PowerW = s.Panel.PowerW,
            UnitPrice = 999m,
        });
        Assert.Equal(HttpStatusCode.OK, catalogUpdate.StatusCode);
        using var sales = Sales();

        var response = await sales.PostAsync($"/api/designs/{design.Id}/quote", null);
        var quote = await response.ReadAsync<QuoteResponse>(HttpStatusCode.Created);

        Assert.Equal($"/api/quotes/{quote.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(s.Customer.Id, quote.CustomerId);
        Assert.Equal(design.Id, quote.DesignId);
        Assert.Equal(1, quote.DesignVersionNumber);
        Assert.False(quote.NeedsRecalculation);
        Assert.Equal(_salesId, quote.CreatedBy);
        Assert.Equal(["PAN-450", "INV-5K"], quote.Lines.Select(l => l.Code));
        Assert.Equal([12m, 1m], quote.Lines.Select(l => l.Quantity));
        Assert.Equal([120.5m, 950m], quote.Lines.Select(l => l.UnitPrice));
        Assert.Equal(2396m, quote.NetTotal);
        Assert.Equal(551.08m, quote.VatTotal);
        Assert.Equal(2947.08m, quote.GrossTotal);

        var fetched = await sales.GetJsonAsync<QuoteResponse>($"/api/quotes/{quote.Id}");
        Assert.Equal(design.Id, fetched.DesignId);
    }

    [Fact]
    public async Task CreateQuote_UsesApprovedVersionNotLaterOnes()
    {
        var s = await SetupAsync();
        var design = await ApprovedDesignAsync(s, panelCount: 12);
        using var sales = Sales();
        using var designer = Designer();
        await sales.PostAsJsonAsync($"/api/designs/{design.Id}/rework", new ReworkDesignRequest { Reason = "Väčší výkon" }, Json);
        await SaveVersionAsync(designer, design.Id, Config(s, 14));
        await sales.PostAsync($"/api/designs/{design.Id}/approve", null);

        var quote = await QuoteFromDesignAsync(sales, design.Id);

        Assert.Equal(2, quote.DesignVersionNumber);
        Assert.Equal(14m, quote.Lines[0].Quantity);
    }

    [Theory]
    [InlineData(FveDesignStatus.Requested)]
    [InlineData(FveDesignStatus.SiteSurveyDone)]
    [InlineData(FveDesignStatus.InDesign)]
    public async Task CreateQuote_FromDesignNotApproved_Returns409(FveDesignStatus status)
    {
        var s = await SetupAsync();
        using var sales = Sales();
        using var designer = Designer();
        var design = await sales.PostAsJsonAsync("/api/designs", CreateRequest() with { CustomerId = s.Customer.Id }, Json)
            .ContinueWith(t => t.Result.ReadAsync<FveDesignResponse>(HttpStatusCode.Created)).Unwrap();
        if (status >= FveDesignStatus.SiteSurveyDone)
        {
            await designer.SurveyAsync(design.Id);
        }

        if (status == FveDesignStatus.InDesign)
        {
            await designer.SetParametersAsync(design.Id);
            await SaveVersionAsync(designer, design.Id, Config(s));
        }

        var response = await sales.PostAsync($"/api/designs/{design.Id}/quote", null);

        await response.ReadAsync<ProblemDetails>(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateQuote_AfterApprovalWithdrawn_Returns409()
    {
        var design = await ApprovedDesignAsync(await SetupAsync());
        using var sales = Sales();
        await sales.PostAsJsonAsync($"/api/designs/{design.Id}/rework", new ReworkDesignRequest { Reason = "Zmena" }, Json);

        var response = await sales.PostAsync($"/api/designs/{design.Id}/quote", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_ForArchivedCustomer_Returns409()
    {
        var s = await SetupAsync();
        var design = await ApprovedDesignAsync(s);
        using var sales = Sales();
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/customers/{s.Customer.Id}/archive", null)).StatusCode);

        var response = await sales.PostAsync($"/api/designs/{design.Id}/quote", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_ForUnknownCustomer_Returns400()
    {
        var s = await SetupAsync();
        using var sales = Sales();
        using var designer = Designer();
        var design = await sales.CreateDesignAsync(); // random customer id that does not exist
        await designer.SurveyAsync(design.Id);
        await designer.SetParametersAsync(design.Id);
        await SaveVersionAsync(designer, design.Id, Config(s));
        await sales.PostAsync($"/api/designs/{design.Id}/approve", null);

        var response = await sales.PostAsync($"/api/designs/{design.Id}/quote", null);

        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);
        Assert.Contains("CustomerId", problem.Errors.Keys);
    }

    [Fact]
    public async Task CreateQuote_UnknownDesign_Returns404_AndAnonymousReturns401()
    {
        using var sales = Sales();
        using var anonymous = _factory.CreateApiClient();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.NotFound, (await sales.PostAsync($"/api/designs/{id}/quote", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/designs/{id}/quote", null)).StatusCode);
    }

    [Fact]
    public async Task Rework_ApprovedDesign_ReopensItAndMarksOpenQuotesForRecalculation()
    {
        var s = await SetupAsync();
        var design = await ApprovedDesignAsync(s);
        var otherDesign = await ApprovedDesignAsync(s);
        using var sales = Sales();
        var draft = await QuoteFromDesignAsync(sales, design.Id);
        var sent = await QuoteFromDesignAsync(sales, design.Id);
        await sales.PostAsync($"/api/quotes/{sent.Id}/send", null);
        var accepted = await QuoteFromDesignAsync(sales, design.Id);
        await sales.PostAsync($"/api/quotes/{accepted.Id}/send", null);
        await sales.PostAsync($"/api/quotes/{accepted.Id}/accept", null);
        var unrelated = await QuoteFromDesignAsync(sales, otherDesign.Id);

        var response = await sales.PostAsJsonAsync(
            $"/api/designs/{design.Id}/rework", new ReworkDesignRequest { Reason = "  Zákazník chce batériu  " }, Json);
        var reworked = await response.ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

        Assert.Equal(FveDesignStatus.InDesign, reworked.Status);
        Assert.Null(reworked.Approval);
        Assert.Equal("Zákazník chce batériu", reworked.LastRework?.Reason);
        Assert.Equal(_salesId, reworked.LastRework?.RequestedBy);

        Assert.True((await sales.GetJsonAsync<QuoteResponse>($"/api/quotes/{draft.Id}")).NeedsRecalculation);
        var sentAfter = await sales.GetJsonAsync<QuoteResponse>($"/api/quotes/{sent.Id}");
        Assert.True(sentAfter.NeedsRecalculation);
        Assert.Equal(QuoteStatus.Sent, sentAfter.Status);
        Assert.False((await sales.GetJsonAsync<QuoteResponse>($"/api/quotes/{accepted.Id}")).NeedsRecalculation);
        Assert.False((await sales.GetJsonAsync<QuoteResponse>($"/api/quotes/{unrelated.Id}")).NeedsRecalculation);
    }

    [Fact]
    public async Task Rework_DesignInDesign_StaysInDesign()
    {
        var design = await InDesignWithVersionAsync(await SetupAsync());
        using var sales = Sales();

        var response = await sales.PostAsJsonAsync($"/api/designs/{design.Id}/rework", new ReworkDesignRequest { Reason = "Doplniť tienenie" }, Json);
        var reworked = await response.ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

        Assert.Equal(FveDesignStatus.InDesign, reworked.Status);
        Assert.Equal("Doplniť tienenie", reworked.LastRework?.Reason);
    }

    [Fact]
    public async Task Rework_BeforeDesignPhase_Returns409()
    {
        using var sales = Sales();
        var design = await sales.CreateDesignAsync();

        var response = await sales.PostAsJsonAsync($"/api/designs/{design.Id}/rework", new ReworkDesignRequest { Reason = "x" }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rework_WithoutReason_Returns400()
    {
        var design = await InDesignWithVersionAsync(await SetupAsync());
        using var sales = Sales();

        var response = await sales.PostAsJsonAsync($"/api/designs/{design.Id}/rework", new ReworkDesignRequest(), Json);

        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);
        Assert.Contains("Reason", problem.Errors.Keys);
    }

    [Fact]
    public async Task Rework_UnknownDesign_Returns404_AndAnonymousReturns401()
    {
        using var sales = Sales();
        using var anonymous = _factory.CreateApiClient();
        var id = Guid.CreateVersion7();
        var body = new ReworkDesignRequest { Reason = "x" };

        Assert.Equal(HttpStatusCode.NotFound, (await sales.PostAsJsonAsync($"/api/designs/{id}/rework", body, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/designs/{id}/rework", body, Json)).StatusCode);
    }
}
