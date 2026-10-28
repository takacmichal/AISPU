using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using Data = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.ConnectionRequests;

/// <summary>Submission, state and proposal deadline of a connection request (#86).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ConnectionRequestDeadlineEndpointsTests : IDisposable
{
    // Tuesday; 10 working days later is Tuesday 2026-11-10, 30 calendar days later is 2026-11-26.
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private readonly ClockedFactory _factory;

    public ConnectionRequestDeadlineEndpointsTests(PostgresFixture postgres) => _factory = new ClockedFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private HttpClient Designer() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);

    private async Task<ConnectionRequestResponse> CreateDraftAsync(decimal acPowerKw = 8m)
    {
        using var sales = Sales();
        var installation = (await Data.CreateInstallationAsync(sales, Data.NewInstallationRequest())).Installation;
        var request = new CreateConnectionRequestRequest
        {
            InstallationId = installation.Id,
            EicCode = "24ZZS12345678901",
            RatedAcPowerKw = acPowerKw,
            MainBreakerAmps = 25,
            Attachments =
            [
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.SingleLineDiagram, FileName = "schema.pdf", ContentType = "application/pdf", SizeBytes = 100 },
                new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.OwnerConsent, FileName = "suhlas.pdf", ContentType = "application/pdf", SizeBytes = 100 },
            ],
        };
        return (await (await sales.PostJsonAsync("/api/connection-requests", request)).ReadJsonAsync<ConnectionRequestResponse>())!;
    }

    private async Task<ConnectionRequestResponse> SubmitAsync(Guid id)
    {
        using var designer = Designer();
        var response = await designer.PostAsync($"/api/connection-requests/{id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync<ConnectionRequestResponse>())!;
    }

    // Distributor decisions arrive with the simulator (#87); here the entity is moved directly.
    private Task DecideAsync(Guid id, Action<ConnectionRequest> decide) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var entity = await db.Set<ConnectionRequest>().SingleAsync(x => x.Id == id);
            decide(entity);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Draft_HasNoDeadline()
    {
        var draft = await CreateDraftAsync();

        Assert.Equal(ConnectionRequestState.Draft, draft.State);
        Assert.Null(draft.SubmittedAt);
        Assert.Null(draft.ProposalDueDate);
        Assert.Null(draft.ProposalRemainingDays);
        Assert.False(draft.ProposalOverdue);
    }

    [Fact]
    public async Task Submit_SmallSource_StartsTenWorkingDayDeadline()
    {
        var draft = await CreateDraftAsync(8m);

        var submitted = await SubmitAsync(draft.Id);

        Assert.Equal(ConnectionRequestState.Submitted, submitted.State);
        Assert.Equal(_clock.GetUtcNow(), submitted.SubmittedAt);
        Assert.Equal(new DateOnly(2026, 11, 10), submitted.ProposalDueDate);
        Assert.Equal(14, submitted.ProposalRemainingDays);
        Assert.False(submitted.ProposalOverdue);
    }

    [Fact]
    public async Task Submit_LargeSource_StartsThirtyCalendarDayDeadline()
    {
        var draft = await CreateDraftAsync(15m);

        var submitted = await SubmitAsync(draft.Id);

        Assert.Equal(new DateOnly(2026, 11, 26), submitted.ProposalDueDate);
        Assert.Equal(30, submitted.ProposalRemainingDays);
    }

    [Fact]
    public async Task Submit_WhenAlreadySubmitted_Returns409_AndUnknownIs404()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        using var designer = Designer();

        var again = await designer.PostAsync($"/api/connection-requests/{draft.Id}/submit", null);
        var missing = await designer.PostAsync($"/api/connection-requests/{Guid.CreateVersion7()}/submit", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Submitted_RequestCannotBeEdited()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        using var sales = Sales();

        var response = await sales.PutJsonAsync($"/api/connection-requests/{draft.Id}", new UpdateConnectionRequestRequest
        {
            EicCode = "24ZZS12345678901",
            RatedAcPowerKw = 8m,
            Attachments = draft.Attachments.Select(a => new AttachmentRequest
            {
                Kind = a.Kind, FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes,
            }).ToList(),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deadline_CountsDownAndFlagsOverdue()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        using var sales = Sales();

        _clock.Advance(TimeSpan.FromDays(14)); // 2026-11-10, the due date itself
        var onDueDate = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{draft.Id}");
        _clock.Advance(TimeSpan.FromDays(1));
        var late = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{draft.Id}");

        Assert.Equal(0, onDueDate.ProposalRemainingDays);
        Assert.False(onDueDate.ProposalOverdue);
        Assert.Equal(-1, late.ProposalRemainingDays);
        Assert.True(late.ProposalOverdue);
    }

    [Fact]
    public async Task List_OverdueFilter_ReturnsOnlyRequestsPastTheirDeadline()
    {
        var overdue = await CreateDraftAsync(8m);
        await SubmitAsync(overdue.Id);
        var inTime = await CreateDraftAsync(15m);
        await SubmitAsync(inTime.Id);
        var draft = await CreateDraftAsync();
        using var sales = Sales();

        _clock.Advance(TimeSpan.FromDays(20)); // small source late, large source still has 10 days

        var late = await sales.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests?overdue=true");
        var all = await sales.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests");

        Assert.Equal([overdue.Id], late.Select(x => x.Id));
        Assert.True(all.Single(x => x.Id == overdue.Id).ProposalOverdue);
        Assert.False(all.Single(x => x.Id == inTime.Id).ProposalOverdue);
        Assert.Equal(10, all.Single(x => x.Id == inTime.Id).ProposalRemainingDays);
        Assert.Contains(all, x => x.Id == draft.Id);
    }

    [Fact]
    public async Task Decision_ClosesTheDeadline_AndExposesState()
    {
        var info = await CreateDraftAsync();
        var rejected = await CreateDraftAsync();
        var offered = await CreateDraftAsync();
        foreach (var id in new[] { info.Id, rejected.Id, offered.Id })
        {
            await SubmitAsync(id);
        }

        _clock.Advance(TimeSpan.FromDays(20)); // past the due date, but a decision has been made
        var now = _clock.GetUtcNow();
        await DecideAsync(info.Id, x => x.RequestInfo("Missing datasheet", now));
        await DecideAsync(rejected.Id, x => x.Reject("No grid capacity", now));
        await DecideAsync(offered.Id, x => x.OfferContract(null, now));
        using var sales = Sales();

        var infoBody = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{info.Id}");
        var rejectedBody = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{rejected.Id}");
        var offeredBody = await sales.GetJsonAsync<ConnectionRequestResponse>($"/api/connection-requests/{offered.Id}");
        var overdue = await sales.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests?overdue=true");

        Assert.Equal(ConnectionRequestState.InfoRequested, infoBody.State);
        Assert.Equal("Missing datasheet", infoBody.DecisionNote);
        Assert.Equal(now, infoBody.DecidedAt);
        Assert.Equal(ConnectionRequestState.Rejected, rejectedBody.State);
        Assert.Equal("No grid capacity", rejectedBody.DecisionNote);
        Assert.Equal(ConnectionRequestState.ContractOffered, offeredBody.State);
        Assert.Null(offeredBody.DecisionNote);
        Assert.All(new[] { infoBody, rejectedBody, offeredBody }, x =>
        {
            Assert.Null(x.ProposalRemainingDays);
            Assert.False(x.ProposalOverdue);
        });
        Assert.Empty(overdue);
    }

    [Fact]
    public async Task Resubmit_AfterInfoRequested_RestartsDeadline()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        _clock.Advance(TimeSpan.FromDays(1)); // Wednesday 2026-10-28
        await DecideAsync(draft.Id, x => x.RequestInfo("Missing datasheet", _clock.GetUtcNow()));
        using var sales = Sales();

        var response = await sales.PostAsync($"/api/connection-requests/{draft.Id}/resubmit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestState.Submitted, body!.State);
        Assert.Equal(new DateOnly(2026, 11, 11), body.ProposalDueDate); // 10 working days after Wednesday 28 Oct
        Assert.Null(body.DecisionNote);
        Assert.Equal(14, body.ProposalRemainingDays);
    }

    [Fact]
    public async Task Resubmit_WhenNotInfoRequested_Returns409()
    {
        var draft = await CreateDraftAsync();
        using var sales = Sales();

        var fromDraft = await sales.PostAsync($"/api/connection-requests/{draft.Id}/resubmit", null);
        await SubmitAsync(draft.Id);
        var fromSubmitted = await sales.PostAsync($"/api/connection-requests/{draft.Id}/resubmit", null);

        Assert.Equal(HttpStatusCode.Conflict, fromDraft.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, fromSubmitted.StatusCode);
    }

    [Fact]
    public async Task InfoRequested_RequestCanBeCorrectedBeforeResubmission()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        await DecideAsync(draft.Id, x => x.RequestInfo("Wrong EIC", _clock.GetUtcNow()));
        using var sales = Sales();

        var response = await sales.PutJsonAsync($"/api/connection-requests/{draft.Id}", new UpdateConnectionRequestRequest
        {
            EicCode = "24ZZS99999999999",
            RatedAcPowerKw = 8m,
            Attachments = draft.Attachments.Select(a => new AttachmentRequest
            {
                Kind = a.Kind, FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes,
            }).ToList(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestState.InfoRequested, body!.State);
        Assert.Equal("24ZZS99999999999", body.EicCode);
    }

    [Fact]
    public async Task ContractSteps_FollowTheOrderOfTheProcess()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        using var sales = Sales();
        using var designer = Designer();
        var url = $"/api/connection-requests/{draft.Id}";

        // Nothing to review or sign before the distributor offers a contract.
        Assert.Equal(HttpStatusCode.Conflict, (await designer.PostAsync($"{url}/review-conditions", null)).StatusCode);

        await DecideAsync(draft.Id, x => x.OfferContract(null, _clock.GetUtcNow()));

        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"{url}/send-contract", null)).StatusCode);
        var reviewed = await (await designer.PostAsync($"{url}/review-conditions", null)).ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(_clock.GetUtcNow(), reviewed!.ConditionsReviewedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"{url}/customer-signed", null)).StatusCode);

        var sent = await (await sales.PostAsync($"{url}/send-contract", null)).ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(_clock.GetUtcNow(), sent!.ContractSentAt);
        Assert.Equal(ConnectionRequestState.ContractOffered, sent.State);
        Assert.Equal(HttpStatusCode.Conflict, (await designer.PostAsync($"{url}/submit-signed-contract", null)).StatusCode);

        var signed = await (await sales.PostAsync($"{url}/customer-signed", null)).ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestState.ContractSigned, signed!.State);

        var submittedContract = await (await designer.PostAsync($"{url}/submit-signed-contract", null)).ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestState.ContractSubmitted, submittedContract!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"{url}/customer-signed", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sales.PostAsync($"/api/connection-requests/{Guid.CreateVersion7()}/send-contract", null)).StatusCode);
    }

    [Fact]
    public async Task Transitions_RequireAuthentication_AndRole()
    {
        var draft = await CreateDraftAsync();
        using var anonymous = _factory.CreateApiClient();
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        using var sales = Sales();

        var submit = $"/api/connection-requests/{draft.Id}/submit";
        var resubmit = $"/api/connection-requests/{draft.Id}/resubmit";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(submit, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(resubmit, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.PostAsync(submit, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(submit, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(resubmit, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync(submit, null)).StatusCode);

        foreach (var step in new[] { "review-conditions", "send-contract", "customer-signed", "submit-signed-contract" })
        {
            var stepUrl = $"/api/connection-requests/{draft.Id}/{step}";
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(stepUrl, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await technician.PostAsync(stepUrl, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(stepUrl, null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync($"/api/connection-requests/{draft.Id}/review-conditions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync($"/api/connection-requests/{draft.Id}/submit-signed-contract", null)).StatusCode);
    }

    [Fact]
    public async Task Customer_SeesDeadlineOfOwnRequestOnly()
    {
        var draft = await CreateDraftAsync();
        await SubmitAsync(draft.Id);
        using var foreignCustomer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        var response = await foreignCustomer.GetAsync($"/api/connection-requests/{draft.Id}");
        var overdue = await foreignCustomer.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests?overdue=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(overdue);
    }

    private sealed class ClockedFactory(PostgresFixture postgres, TimeProvider clock) : ApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }
}
