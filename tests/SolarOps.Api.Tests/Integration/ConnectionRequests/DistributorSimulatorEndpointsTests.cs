using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using Data = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.ConnectionRequests;

/// <summary>Mock of the distributor's answer (#87): decision and conclusion drive the same transitions as the real flow.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DistributorSimulatorEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private ClockedFactory? _factory;
    private ClockedFactory Factory => _factory ??= new ClockedFactory(postgres, _clock, simulatorEnabled: null);

    public void Dispose()
    {
        _factory?.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Operator(ApiFactory? factory = null) => (factory ?? Factory).CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private static async Task<ConnectionRequestResponse> SubmittedAsync(ApiFactory factory, decimal acPowerKw = 8m)
    {
        using var sales = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var designer = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        var installation = (await Data.CreateInstallationAsync(sales, Data.NewInstallationRequest())).Installation;
        var request = new CreateConnectionRequestRequest
        {
            InstallationId = installation.Id,
            EicCode = "24ZZS12345678901",
            RatedAcPowerKw = acPowerKw,
            Attachments =
            [
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.SingleLineDiagram, FileName = "schema.pdf", ContentType = "application/pdf", SizeBytes = 100 },
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.OwnerConsent, FileName = "suhlas.pdf", ContentType = "application/pdf", SizeBytes = 100 },
            ],
        };
        var draft = (await (await sales.PostJsonAsync("/api/connection-requests", request)).ReadJsonAsync<ConnectionRequestResponse>())!;
        return await (await designer.PostAsync($"/api/connection-requests/{draft.Id}/submit", null)).ReadJsonAsync<ConnectionRequestResponse>();
    }

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid id, DistributorDecision? decision, string? note = null) =>
        client.PostJsonAsync($"/api/connection-requests/{id}/decision", new DistributorDecisionRequest { Decision = decision, Note = note });

    [Fact]
    public async Task Approve_MovesSubmittedRequestToContractOffered_AndStopsTheDeadline()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();
        _clock.Advance(TimeSpan.FromDays(2));

        var response = await DecideAsync(op, submitted.Id, DistributorDecision.Approve);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestState.ContractOffered, body.State);
        Assert.Equal(_clock.GetUtcNow(), body.DecidedAt);
        Assert.Null(body.ProposalRemainingDays);
        Assert.False(body.ProposalOverdue);
    }

    [Fact]
    public async Task RequestInfo_MovesToInfoRequested_WithNote_ThenResubmitRestartsTheProcess()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();
        using var sales = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

        var body = await (await DecideAsync(op, submitted.Id, DistributorDecision.RequestInfo, "  Missing datasheet  ")).ReadJsonAsync<ConnectionRequestResponse>();
        var resubmitted = await (await sales.PostAsync($"/api/connection-requests/{submitted.Id}/resubmit", null)).ReadJsonAsync<ConnectionRequestResponse>();

        Assert.Equal(ConnectionRequestState.InfoRequested, body.State);
        Assert.Equal("Missing datasheet", body.DecisionNote);
        Assert.Equal(ConnectionRequestState.Submitted, resubmitted.State);
        Assert.Null(resubmitted.DecisionNote);
    }

    [Fact]
    public async Task Reject_MovesToRejected_WithReason()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();

        var body = await (await DecideAsync(op, submitted.Id, DistributorDecision.Reject, "No grid capacity")).ReadJsonAsync<ConnectionRequestResponse>();

        Assert.Equal(ConnectionRequestState.Rejected, body.State);
        Assert.Equal("No grid capacity", body.DecisionNote);
    }

    [Fact]
    public async Task Decision_MatchesTheDomainTransition()
    {
        var viaApi = await SubmittedAsync(Factory);
        var viaDomain = await SubmittedAsync(Factory);
        using var op = Operator();
        var now = _clock.GetUtcNow();

        var api = await (await DecideAsync(op, viaApi.Id, DistributorDecision.Reject, "No grid capacity")).ReadJsonAsync<ConnectionRequestResponse>();
        await Factory.ExecuteDbAsync(async db =>
        {
            var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.Set<ConnectionRequest>(), x => x.Id == viaDomain.Id);
            entity.Reject("No grid capacity", now);
            await db.SaveChangesAsync();
        });
        using var sales = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var domain = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{viaDomain.Id}");

        Assert.Equal(domain.State, api.State);
        Assert.Equal(domain.DecisionNote, api.DecisionNote);
        Assert.Equal(domain.DecidedAt, api.DecidedAt);
        Assert.Equal(domain.ProposalRemainingDays, api.ProposalRemainingDays);
    }

    [Fact]
    public async Task Decision_WithoutNote_ForRequestInfoAndReject_Returns400()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();

        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(op, submitted.Id, DistributorDecision.Reject)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(op, submitted.Id, DistributorDecision.RequestInfo, "   ")).StatusCode);
        var stillSubmitted = await op.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{submitted.Id}");
        Assert.Equal(ConnectionRequestState.Submitted, stillSubmitted.State);
    }

    [Fact]
    public async Task Decision_WithInvalidBody_Returns400()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();

        var noDecision = await DecideAsync(op, submitted.Id, null);
        var tooLong = await DecideAsync(op, submitted.Id, DistributorDecision.Reject, new string('x', ConnectionRequest.DecisionNoteMaxLength + 1));
        var unknown = await op.PostAsync($"/api/connection-requests/{submitted.Id}/decision",
            new StringContent("""{"decision":"Maybe"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, noDecision.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Decision_OnRequestThatIsNotSubmitted_Returns409_AndUnknownIs404()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();
        await DecideAsync(op, submitted.Id, DistributorDecision.Reject, "No grid capacity");

        var again = await DecideAsync(op, submitted.Id, DistributorDecision.Approve);
        var missing = await DecideAsync(op, Guid.CreateVersion7(), DistributorDecision.Approve);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task WholeProcess_CanBeDemonstratedWithoutARealDistributor()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();
        using var sales = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var designer = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        var url = $"/api/connection-requests/{submitted.Id}";

        await DecideAsync(op, submitted.Id, DistributorDecision.Approve);
        await designer.PostAsync($"{url}/review-conditions", null);
        await sales.PostAsync($"{url}/send-contract", null);
        await sales.PostAsync($"{url}/customer-signed", null);
        await designer.PostAsync($"{url}/submit-signed-contract", null);
        var concluded = await op.PostAsync($"{url}/conclude", null);

        Assert.Equal(HttpStatusCode.OK, concluded.StatusCode);
        Assert.Equal(ConnectionRequestState.Concluded, (await concluded.ReadJsonAsync<ConnectionRequestResponse>()).State);
        Assert.Equal(HttpStatusCode.Conflict, (await op.PostAsync($"{url}/conclude", null)).StatusCode);
    }

    [Fact]
    public async Task Conclude_BeforeTheSignedContractIsSubmitted_Returns409()
    {
        var submitted = await SubmittedAsync(Factory);
        using var op = Operator();
        await DecideAsync(op, submitted.Id, DistributorDecision.Approve);

        var response = await op.PostAsync($"/api/connection-requests/{submitted.Id}/conclude", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Simulator_RequiresAuthentication_AndBackOfficeRole()
    {
        var submitted = await SubmittedAsync(Factory);
        using var anonymous = Factory.CreateApiClient();
        using var admin = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);
        using var sales = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var designer = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var customer = Factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        var url = $"/api/connection-requests/{submitted.Id}";

        foreach (var path in new[] { "decision", "conclude" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"{url}/{path}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync($"{url}/{path}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await designer.PostAsync($"{url}/{path}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync($"{url}/{path}", null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, submitted.Id, DistributorDecision.Approve)).StatusCode);
    }

    [Fact]
    public async Task Simulator_IsOffInProduction_UnlessEnabledExplicitly()
    {
        using var production = new ClockedFactory(postgres, _clock, simulatorEnabled: null, environment: "Production");
        using var forcedOn = new ClockedFactory(postgres, _clock, simulatorEnabled: true, environment: "Production");
        using var forcedOff = new ClockedFactory(postgres, _clock, simulatorEnabled: false);
        var fromProduction = await SubmittedAsync(production);
        var fromForcedOn = await SubmittedAsync(forcedOn);
        var fromForcedOff = await SubmittedAsync(forcedOff);

        using var productionClient = Operator(production);
        using var forcedOnClient = Operator(forcedOn);
        using var forcedOffClient = Operator(forcedOff);

        Assert.Equal(HttpStatusCode.NotFound, (await DecideAsync(productionClient, fromProduction.Id, DistributorDecision.Approve)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.PostAsync($"/api/connection-requests/{fromProduction.Id}/conclude", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(forcedOnClient, fromForcedOn.Id, DistributorDecision.Approve)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DecideAsync(forcedOffClient, fromForcedOff.Id, DistributorDecision.Approve)).StatusCode);
    }

    private sealed class ClockedFactory(PostgresFixture postgres, TimeProvider clock, bool? simulatorEnabled, string? environment = null)
        : ApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            if (simulatorEnabled is { } enabled)
            {
                builder.UseSetting("ConnectionRequest:Simulator:Enabled", enabled.ToString());
            }

            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }
}
