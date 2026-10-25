using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Quotes;

[Collection(PostgresCollection.Name)]
public sealed class QuoteWorkflowTests : IDisposable
{
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero));
    private readonly ClockedFactory _factory;

    public QuoteWorkflowTests(PostgresFixture postgres) => _factory = new ClockedFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales(Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), Roles.Sales);

    private async Task<QuoteResponse> CreateDraftAsync(HttpClient client)
    {
        var customer = await CustomerTestData.CreateAsync(client, CustomerTestData.Person($"{Guid.NewGuid():N}@example.sk"));
        using var component = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = $"PAN-{Guid.NewGuid():N}",
            Name = "Panel",
            PowerW = 450,
            UnitPrice = 100m,
        });
        var panel = await component.ReadJsonAsync<ComponentResponse>();

        using var response = await client.PostJsonAsync("/api/quotes", new
        {
            customerId = customer.Id,
            lines = new[] { new { componentId = panel.Id, quantity = 2 } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<QuoteResponse>();
    }

    private static async Task<QuoteResponse> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.ReadJsonAsync<QuoteResponse>();
    }

    [Fact]
    public async Task Send_MovesToSent_WithDefaultValidity_AndListsNextStates()
    {
        using var client = Sales();
        var draft = await CreateDraftAsync(client);
        Assert.Equal([QuoteStatus.Sent], draft.AllowedTransitions);
        Assert.Null(draft.ValidUntil);

        var sent = await ReadAsync(await client.PostAsync($"/api/quotes/{draft.Id}/send", null));

        Assert.Equal(QuoteStatus.Sent, sent.Status);
        Assert.Equal(_clock.GetUtcNow().AddDays(30), sent.ValidUntil);
        Assert.Equal([QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Expired], sent.AllowedTransitions);
    }

    [Fact]
    public async Task Send_WithCustomValidity_UsesIt_AndRejectsOutOfRange()
    {
        using var client = Sales();
        var draft = await CreateDraftAsync(client);

        using var tooLong = await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/send", new { validityDays = 366 });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("ValidityDays", (await tooLong.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);
        using var zero = await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/send", new { validityDays = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var sent = await ReadAsync(await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/send", new { validityDays = 10 }));
        Assert.Equal(_clock.GetUtcNow().AddDays(10), sent.ValidUntil);
    }

    [Fact]
    public async Task Accept_And_Reject_OnlyFromSent_AndAreFinal()
    {
        using var client = Sales();
        var draft = await CreateDraftAsync(client);

        using var acceptDraft = await client.PostAsync($"/api/quotes/{draft.Id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, acceptDraft.StatusCode);
        using var rejectDraft = await client.PostAsync($"/api/quotes/{draft.Id}/reject", null);
        Assert.Equal(HttpStatusCode.Conflict, rejectDraft.StatusCode);

        await client.PostAsync($"/api/quotes/{draft.Id}/send", null);
        var accepted = await ReadAsync(await client.PostAsync($"/api/quotes/{draft.Id}/accept", null));
        Assert.Equal(QuoteStatus.Accepted, accepted.Status);
        Assert.Empty(accepted.AllowedTransitions);

        foreach (var action in new[] { "send", "accept", "reject" })
        {
            using var again = await client.PostAsync($"/api/quotes/{draft.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        var other = await CreateDraftAsync(client);
        await client.PostAsync($"/api/quotes/{other.Id}/send", null);
        var rejected = await ReadAsync(await client.PostAsJsonAsync($"/api/quotes/{other.Id}/reject", new { note = "Príliš drahé" }));
        Assert.Equal(QuoteStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task History_RecordsEveryChange_WithUserAndNote_OldestFirst()
    {
        var userId = Guid.CreateVersion7();
        using var client = Sales(userId);
        var draft = await CreateDraftAsync(client);
        _clock.Advance(TimeSpan.FromHours(1));
        await client.PostAsync($"/api/quotes/{draft.Id}/send", null);
        _clock.Advance(TimeSpan.FromHours(1));
        await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/accept", new { note = "Telefonicky" });

        var history = await client.GetJsonAsync<List<QuoteStateChangeResponse>>($"/api/quotes/{draft.Id}/history");

        Assert.Equal([null, QuoteStatus.Draft, QuoteStatus.Sent], history.Select(h => h.FromStatus));
        Assert.Equal([QuoteStatus.Draft, QuoteStatus.Sent, QuoteStatus.Accepted], history.Select(h => h.ToStatus));
        Assert.All(history, h => Assert.Equal(userId, h.ChangedBy));
        Assert.Equal("Telefonicky", history[2].Note);
        Assert.True(history[0].ChangedAt < history[1].ChangedAt && history[1].ChangedAt < history[2].ChangedAt);
    }

    [Fact]
    public async Task Accept_AfterValidityEnded_Returns409_AndMarksQuoteExpired()
    {
        using var client = Sales();
        var draft = await CreateDraftAsync(client);
        await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/send", new { validityDays = 7 });
        _clock.Advance(TimeSpan.FromDays(7));

        using var response = await client.PostAsync($"/api/quotes/{draft.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reloaded = await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{draft.Id}");
        Assert.Equal(QuoteStatus.Expired, reloaded.Status);
    }

    [Fact]
    public async Task ExpireOverdue_ExpiresOnlySentQuotesPastValidity_AndRecordsSystemChange()
    {
        using var client = Sales();
        var shortLived = await CreateDraftAsync(client);
        var longLived = await CreateDraftAsync(client);
        var stillDraft = await CreateDraftAsync(client);
        await client.PostAsJsonAsync($"/api/quotes/{shortLived.Id}/send", new { validityDays = 5 });
        await client.PostAsJsonAsync($"/api/quotes/{longLived.Id}/send", new { validityDays = 30 });
        _clock.Advance(TimeSpan.FromDays(5));

        int expired;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<IQuoteService>().ExpireOverdueAsync(CancellationToken.None);
        }

        Assert.Equal(1, expired);
        Assert.Equal(QuoteStatus.Expired, (await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{shortLived.Id}")).Status);
        Assert.Equal(QuoteStatus.Sent, (await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{longLived.Id}")).Status);
        Assert.Equal(QuoteStatus.Draft, (await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{stillDraft.Id}")).Status);

        var history = await client.GetJsonAsync<List<QuoteStateChangeResponse>>($"/api/quotes/{shortLived.Id}/history");
        var last = history[^1];
        Assert.Equal(QuoteStatus.Expired, last.ToStatus);
        Assert.Null(last.ChangedBy);
    }

    [Fact]
    public async Task Transition_NoteTooLong_Returns400()
    {
        using var client = Sales();
        var draft = await CreateDraftAsync(client);

        using var response = await client.PostAsJsonAsync($"/api/quotes/{draft.Id}/send", new { note = new string('x', Quote.NoteMaxLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transitions_UnknownQuote_Returns404()
    {
        using var client = Sales();
        var id = Guid.CreateVersion7();

        foreach (var action in new[] { "send", "accept", "reject" })
        {
            using var response = await client.PostAsync($"/api/quotes/{id}/{action}", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var history = await client.GetAsync($"/api/quotes/{id}/history");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Transitions_WithoutSalesOrAdminRole_Return403(string role)
    {
        using var sales = Sales();
        var draft = await CreateDraftAsync(sales);
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        foreach (var action in new[] { "send", "accept", "reject" })
        {
            using var response = await client.PostAsync($"/api/quotes/{draft.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task History_ReadableByDesigner_AndAnonymousGets401()
    {
        using var sales = Sales();
        var draft = await CreateDraftAsync(sales);
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var anonymous = _factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync($"/api/quotes/{draft.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/quotes/{draft.Id}/send", null)).StatusCode);
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
