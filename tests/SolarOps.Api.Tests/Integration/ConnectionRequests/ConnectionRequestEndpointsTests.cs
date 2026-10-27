using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using InstallationsData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.ConnectionRequests;

[Collection(PostgresCollection.Name)]
public sealed class ConnectionRequestEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private HttpClient Designer() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);

    private static ConnectionRequestAttachmentRequest File(
        ConnectionAttachmentKind kind,
        string fileName,
        string content = "PDF")
        => new()
        {
            Kind = kind,
            FileName = fileName,
            ContentType = "application/pdf",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
        };

    private static CreateConnectionRequestRequest NewRequest(Guid installationId, decimal powerKw = 9.6m) => new()
    {
        InstallationId = installationId,
        TotalInverterAcPowerKw = powerKw,
        Note = "Žiadosť pre distribučku",
        Attachments =
        [
            File(ConnectionAttachmentKind.SingleLineDiagram, "schema.pdf"),
            File(ConnectionAttachmentKind.OwnerConsent, "suhlas-vlastnika.pdf"),
        ],
    };

    private static async Task<InstallationDetailResponse> NewInstallationAsync(HttpClient client, decimal? powerKwp = 9.6m)
    {
        using var response = await client.PostJsonAsync("/api/installations", InstallationsData.NewInstallationRequest(powerKwp));
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<InstallationDetailResponse>();
    }

    [Fact]
    public async Task Create_ReturnsReadyRequestBoundToInstallation()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Installation.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal($"/api/connection-requests/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(ConnectionRequestStatus.Ready, created.Status);
        Assert.Equal(ConnectionSourceType.SmallSource, created.Type);
        Assert.Equal(installation.Installation.Id, created.InstallationId);
        Assert.Equal(installation.Installation.CustomerId, created.CustomerId);
        Assert.Equal(installation.Installation.SupplyPointId, created.SupplyPointId);
        Assert.Equal(2, created.Attachments.Count);
        Assert.Contains(created.Attachments, a => a.Kind == ConnectionAttachmentKind.SingleLineDiagram);
        Assert.Contains(created.Attachments, a => a.Kind == ConnectionAttachmentKind.OwnerConsent);
        Assert.All(created.Attachments, a => Assert.Equal($"/api/connection-requests/{created.Id}/attachments/{a.Id}", a.DownloadUrl));

        var listed = await client.GetJsonAsync<List<ConnectionRequestResponse>>(
            $"/api/connection-requests?installationId={installation.Installation.Id}");
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);
    }

    [Theory]
    [InlineData("10.8", ConnectionSourceType.SmallSource)]
    [InlineData("10.9", ConnectionSourceType.LargeSource)]
    public async Task Create_DerivesTypeFromTotalInverterAcPower(string power, ConnectionSourceType expected)
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient, powerKwp: null);
        using var client = Sales();
        var request = NewRequest(installation.Installation.Id, decimal.Parse(power, System.Globalization.CultureInfo.InvariantCulture));

        var created = await (await client.PostJsonAsync("/api/connection-requests", request))
            .ReadJsonAsync<ConnectionRequestResponse>();

        Assert.Equal(expected, created.Type);
    }

    [Fact]
    public async Task Create_WithoutOwnerConsent_Returns400_AndStoresNothing()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Sales();
        var request = NewRequest(installation.Installation.Id) with
        {
            Attachments = [File(ConnectionAttachmentKind.SingleLineDiagram, "schema.pdf")],
        };

        using var response = await client.PostJsonAsync("/api/connection-requests", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("Attachments", problem.Errors.Keys);
        Assert.Contains("OwnerConsent", problem.Errors["Attachments"][0]);
        Assert.Empty(await client.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests"));
    }

    [Fact]
    public async Task Create_InvalidBase64OrUnknownInstallation_Returns400()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Sales();

        var invalidBase64 = NewRequest(installation.Installation.Id) with
        {
            Attachments =
            [
                new ConnectionRequestAttachmentRequest
                {
                    Kind = ConnectionAttachmentKind.SingleLineDiagram,
                    FileName = "schema.pdf",
                    ContentBase64 = "not base64",
                },
                File(ConnectionAttachmentKind.OwnerConsent, "suhlas.pdf"),
            ],
        };
        using var badContent = await client.PostJsonAsync("/api/connection-requests", invalidBase64);
        Assert.Equal(HttpStatusCode.BadRequest, badContent.StatusCode);
        Assert.Contains("Attachments[0].ContentBase64", (await badContent.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var missing = await client.PostJsonAsync("/api/connection-requests", NewRequest(Guid.CreateVersion7()));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("InstallationId", (await missing.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);
    }

    [Fact]
    public async Task Create_SecondRequestForSameInstallation_Returns409()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Sales();
        var request = NewRequest(installation.Installation.Id);
        (await client.PostJsonAsync("/api/connection-requests", request)).EnsureSuccessStatusCode();

        using var again = await client.PostJsonAsync("/api/connection-requests", request with { Note = "Druhá" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsAttachmentMetadataWithoutContent_AndDownloadReturnsTheFile()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Sales();
        var created = await (await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Installation.Id)))
            .ReadJsonAsync<ConnectionRequestResponse>();

        using var detailResponse = await client.GetAsync($"/api/connection-requests/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("contentBase64", detailJson, StringComparison.OrdinalIgnoreCase);
        var detail = JsonSerializer.Deserialize<ConnectionRequestResponse>(detailJson, ApiJson.Options)!;

        var diagram = Assert.Single(detail.Attachments, a => a.Kind == ConnectionAttachmentKind.SingleLineDiagram);
        Assert.Equal("schema.pdf", diagram.FileName);
        Assert.Equal("application/pdf", diagram.ContentType);
        Assert.Equal(3L, diagram.SizeBytes);

        using var download = await client.GetAsync(diagram.DownloadUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(download.Content.Headers.ContentDisposition);
        Assert.Contains("schema.pdf", download.Content.Headers.ContentDisposition.ToString());
        Assert.Equal("PDF", Encoding.UTF8.GetString(await download.Content.ReadAsByteArrayAsync()));

        using var unknown = await client.GetAsync($"/api/connection-requests/{created.Id}/attachments/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Sales();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/connection-requests/{Guid.CreateVersion7()}")).StatusCode);
    }

    [Fact]
    public async Task Update_ReplacesAttachmentsAndRecalculatesType()
    {
        using var operatorClient = Operator();
        var installation = await NewInstallationAsync(operatorClient);
        using var client = Designer();
        var created = await (await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Installation.Id)))
            .ReadJsonAsync<ConnectionRequestResponse>();

        var update = new UpdateConnectionRequestRequest
        {
            TotalInverterAcPowerKw = 12.5m,
            Note = "Zmenený výkon",
            Attachments =
            [
                File(ConnectionAttachmentKind.SingleLineDiagram, "schema-v2.pdf"),
                File(ConnectionAttachmentKind.OwnerConsent, "suhlas-v2.pdf"),
                File(ConnectionAttachmentKind.PanelDatasheet, "panel.pdf"),
            ],
        };

        var updated = await (await client.PutJsonAsync($"/api/connection-requests/{created.Id}", update))
            .ReadJsonAsync<ConnectionRequestResponse>();

        Assert.Equal(ConnectionRequestStatus.Ready, updated.Status);
        Assert.Equal(ConnectionSourceType.LargeSource, updated.Type);
        Assert.Equal(12.5m, updated.TotalInverterAcPowerKw);
        Assert.Equal("Zmenený výkon", updated.Note);
        Assert.Equal(3, updated.Attachments.Count);
        Assert.Contains(updated.Attachments, a => a.FileName == "schema-v2.pdf");
        Assert.DoesNotContain(updated.Attachments, a => a.FileName == "schema.pdf");
    }

    [Fact]
    public async Task List_FiltersByStatus_AndRejectsUnknownStatus()
    {
        using var operatorClient = Operator();
        var first = await NewInstallationAsync(operatorClient);
        var second = await NewInstallationAsync(operatorClient);
        using var client = Sales();
        await client.PostJsonAsync("/api/connection-requests", NewRequest(first.Installation.Id));
        await client.PostJsonAsync("/api/connection-requests", NewRequest(second.Installation.Id));

        var ready = await client.GetJsonAsync<List<ConnectionRequestResponse>>("/api/connection-requests?status=Ready");

        Assert.Equal(2, ready.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/connection-requests?status=Nonsense")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAuthenticationAndWriteRole()
    {
        using var anonymous = _factory.CreateApiClient();
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);
        var request = NewRequest(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/connection-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostJsonAsync("/api/connection-requests", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.PostJsonAsync("/api/connection-requests", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await technician.GetAsync("/api/connection-requests")).StatusCode);
    }
}
