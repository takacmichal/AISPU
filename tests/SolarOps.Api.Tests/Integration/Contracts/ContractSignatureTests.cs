using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Contracts;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using static SolarOps.Api.Tests.Integration.Contracts.ContractTestData;

namespace SolarOps.Api.Tests.Integration.Contracts;

[Collection(PostgresCollection.Name)]
public sealed class ContractSignatureTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales(Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), Roles.Sales);

    private HttpClient Admin() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

    private static async Task<ContractResponse> NewDraftContractAsync(HttpClient client)
    {
        var quote = await NewQuoteAsync(client, await NewCustomerAsync(client));
        using var response = await CreateContractAsync(client, quote.Id);
        return await response.ReadJsonAsync<ContractResponse>();
    }

    private static Task<HttpResponseMessage> SignAsync(HttpClient client, Guid contractId, object? body = null) =>
        client.PostAsJsonAsync($"/api/contracts/{contractId}/sign", body ?? new { supplyPointId = Guid.CreateVersion7() });

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid contractId, decimal amount, object? extra = null) =>
        client.PostAsJsonAsync($"/api/contracts/{contractId}/deposit/pay", extra ?? new { amount });

    private static async Task<ContractResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<ContractResponse>();
    }

    [Fact]
    public async Task Sign_SetsSigned_AndCreatesPlannedInstallation()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);
        var supplyPointId = Guid.CreateVersion7();

        var signed = await ReadAsync(await SignAsync(client, contract.Id, new { supplyPointId }));

        Assert.Equal(ContractStatus.Signed, signed.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), signed.SignedOn);
        Assert.Equal(DepositStatus.NotPaid, signed.DepositStatus);
        Assert.NotNull(signed.InstallationId);

        var detail = await client.GetJsonAsync<InstallationDetailResponse>($"/api/installations/{signed.InstallationId}");
        var installation = detail.Installation;
        Assert.Equal(InstallationState.Planned, installation.State);
        Assert.Equal(contract.Id, installation.ContractId);
        Assert.Equal(contract.CustomerId, installation.CustomerId);
        Assert.Equal(supplyPointId, installation.SupplyPointId);
        Assert.Equal($"Inštalácia {contract.Number}", installation.Name);
        var byContract = await client.GetJsonAsync<List<InstallationResponse>>($"/api/installations?contractId={contract.Id}");
        Assert.Single(byContract);
    }

    [Fact]
    public async Task Sign_WithCustomNameAndPastDate_UsesThem()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);

        var signed = await ReadAsync(await SignAsync(client, contract.Id, new
        {
            supplyPointId = Guid.CreateVersion7(),
            signedOn = "2026-01-15",
            installationName = "Rodinný dom Bratislava",
        }));

        Assert.Equal(new DateOnly(2026, 1, 15), signed.SignedOn);
        var detail = await client.GetJsonAsync<InstallationDetailResponse>($"/api/installations/{signed.InstallationId}");
        Assert.Equal("Rodinný dom Bratislava", detail.Installation.Name);
    }

    [Fact]
    public async Task Sign_InvalidRequest_Returns400_AndLeavesContractDraft()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);

        using var future = await SignAsync(client, contract.Id, new { supplyPointId = Guid.CreateVersion7(), signedOn = "2999-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("SignedOn", (await future.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var missing = await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/sign", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var reloaded = await client.GetJsonAsync<ContractResponse>($"/api/contracts/{contract.Id}");
        Assert.Equal(ContractStatus.Draft, reloaded.Status);
        Assert.Null(reloaded.InstallationId);
        Assert.Empty(await client.GetJsonAsync<List<InstallationResponse>>("/api/installations"));
    }

    [Fact]
    public async Task Sign_Twice_Returns409_AndCreatesOnlyOneInstallation()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);
        (await SignAsync(client, contract.Id)).EnsureSuccessStatusCode();

        using var again = await SignAsync(client, contract.Id);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(await client.GetJsonAsync<List<InstallationResponse>>("/api/installations"));
    }

    [Fact]
    public async Task Sign_ConcurrentRequests_SignOnce()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SignAsync(client, contract.Id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await client.GetJsonAsync<List<InstallationResponse>>("/api/installations"));
        var audit = await client.GetJsonAsync<List<ContractAuditEntryResponse>>($"/api/contracts/{contract.Id}/audit");
        Assert.Single(audit, e => e.Action == ContractAction.Signed);
    }

    [Fact]
    public async Task Deposit_BeforeSignature_Returns409()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);

        using var response = await PayAsync(client, contract.Id, 100m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deposit_RecordsAmountAndDate_AndCanBePaidOnlyOnce()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);
        (await SignAsync(client, contract.Id)).EnsureSuccessStatusCode();

        var paid = await ReadAsync(await PayAsync(client, contract.Id, 70.50m, new { amount = 70.50m, paidOn = "2026-01-20" }));

        Assert.Equal(DepositStatus.Paid, paid.DepositStatus);
        Assert.Equal(70.50m, paid.DepositPaidAmount);
        Assert.Equal(new DateOnly(2026, 1, 20), paid.DepositPaidOn);
        Assert.Equal(ContractStatus.Signed, paid.Status);

        using var twice = await PayAsync(client, contract.Id, 10m);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10000)]
    [InlineData(10.123)]
    public async Task Deposit_InvalidAmount_Returns400(double amount)
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);
        (await SignAsync(client, contract.Id)).EnsureSuccessStatusCode();

        using var response = await PayAsync(client, contract.Id, (decimal)amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Amount", (await response.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);
        Assert.Equal(DepositStatus.NotPaid, (await client.GetJsonAsync<ContractResponse>($"/api/contracts/{contract.Id}")).DepositStatus);
    }

    [Fact]
    public async Task Deposit_FutureDateOrMissingAmount_Returns400()
    {
        using var client = Sales();
        var contract = await NewDraftContractAsync(client);
        (await SignAsync(client, contract.Id)).EnsureSuccessStatusCode();

        using var future = await PayAsync(client, contract.Id, 50m, new { amount = 50m, paidOn = "2999-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("PaidOn", (await future.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var missing = await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/deposit/pay", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Realization_CannotStartWithoutDeposit_ButStartsAfterIt()
    {
        using var sales = Sales();
        using var admin = Admin();
        var contract = await NewDraftContractAsync(sales);
        var signed = await ReadAsync(await SignAsync(sales, contract.Id));
        var url = $"/api/installations/{signed.InstallationId}/transitions";

        using var blocked = await admin.PostAsJsonAsync(url, new { targetState = "ComponentsOrdered" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var stillPlanned = await admin.GetJsonAsync<InstallationDetailResponse>($"/api/installations/{signed.InstallationId}");
        Assert.Equal(InstallationState.Planned, stillPlanned.Installation.State);

        (await PayAsync(sales, contract.Id, 100m)).EnsureSuccessStatusCode();

        using var allowed = await admin.PostAsJsonAsync(url, new { targetState = "ComponentsOrdered" });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Audit_RecordsCreatedSignedAndDeposit_WithUsers_OldestFirst()
    {
        var creator = Guid.CreateVersion7();
        var signer = Guid.CreateVersion7();
        var cashier = Guid.CreateVersion7();
        using var creatorClient = Sales(creator);
        var contract = await NewDraftContractAsync(creatorClient);
        using var signerClient = Sales(signer);
        using var cashierClient = Sales(cashier);
        (await SignAsync(signerClient, contract.Id)).EnsureSuccessStatusCode();
        (await PayAsync(cashierClient, contract.Id, 100m)).EnsureSuccessStatusCode();

        var audit = await creatorClient.GetJsonAsync<List<ContractAuditEntryResponse>>($"/api/contracts/{contract.Id}/audit");

        Assert.Equal([ContractAction.Created, ContractAction.Signed, ContractAction.DepositPaid], audit.Select(e => e.Action));
        Assert.Equal([creator, signer, cashier], audit.Select(e => e.PerformedBy));
        Assert.Contains("100.00", audit[2].Details);
    }

    [Fact]
    public async Task Audit_UnknownContract_Returns404_AndDesignerCanRead()
    {
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var sales = Sales();
        var contract = await NewDraftContractAsync(sales);

        Assert.Equal(HttpStatusCode.NotFound, (await designer.GetAsync($"/api/contracts/{Guid.CreateVersion7()}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync($"/api/contracts/{contract.Id}/audit")).StatusCode);
    }

    [Fact]
    public async Task SignAndDeposit_UnknownContract_Return404()
    {
        using var client = Sales();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.NotFound, (await SignAsync(client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(client, id, 10m)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task SignAndDeposit_WithoutSalesOrAdminRole_Return403(string role)
    {
        using var sales = Sales();
        var contract = await NewDraftContractAsync(sales);
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        Assert.Equal(HttpStatusCode.Forbidden, (await SignAsync(client, contract.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PayAsync(client, contract.Id, 10m)).StatusCode);
    }

    [Fact]
    public async Task SignAndDeposit_Anonymous_Return401()
    {
        using var client = _factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignAsync(client, Guid.CreateVersion7())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PayAsync(client, Guid.CreateVersion7(), 10m)).StatusCode);
    }
}
