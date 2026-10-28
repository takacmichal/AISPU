using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.DeviceKeys;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.DeviceKeys;

[Collection(PostgresCollection.Name)]
public sealed class DeviceApiKeyTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Admin() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

    private async Task<(Guid Installation, Guid Inverter)> SeedAsync()
    {
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var installation = (await InstallationsTestData.CreateInstallationAsync(op)).Installation.Id;
        using var response = await op.PostJsonAsync($"/api/installations/{installation}/devices", new AddDeviceRequest
        {
            Type = DeviceType.Inverter,
            SerialNumber = $"INV-{Guid.NewGuid():N}",
            Manufacturer = "Fronius",
            Model = "Symo 10.0",
            PowerW = 10000,
        });
        response.EnsureSuccessStatusCode();
        return (installation, (await response.ReadJsonAsync<DeviceResponse>()).Id);
    }

    private static object Batch(Guid installation, Guid inverter) => new
    {
        schemaVersion = "1.0",
        installationId = installation,
        sentAt = "2026-06-21T10:36:00Z",
        source = "gateway",
        readings = new[]
        {
            new { deviceId = inverter, timestamp = "2026-06-21T10:35:00Z", intervalSec = 300, status = "MPPT", acPowerW = 1000.0, totalEnergyWh = 1000.0 },
        },
    };

    private async Task<HttpStatusCode> SendAsync(string apiKey, Guid installation, Guid inverter)
    {
        using var device = _factory.CreateDeviceClient(apiKey);
        using var response = await device.PostJsonAsync("/api/telemetry", Batch(installation, inverter));
        return response.StatusCode;
    }

    [Fact]
    public async Task Issued_key_authenticates_the_device()
    {
        var (installation, inverter) = await SeedAsync();

        var issued = await _factory.IssueKeyAsync(installation);

        Assert.StartsWith("sok_", issued.ApiKey);
        Assert.Equal(installation, issued.InstallationId);
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(issued.ApiKey, installation, inverter));
    }

    [Fact]
    public async Task Issuing_a_second_active_key_is_409()
    {
        var (installation, _) = await SeedAsync();
        await _factory.IssueKeyAsync(installation);
        using var admin = Admin();

        using var response = await admin.PostAsync(DeviceKeyTestApi.KeysUrl(installation), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Plaintext_key_is_not_stored()
    {
        var (installation, _) = await SeedAsync();
        var issued = await _factory.IssueKeyAsync(installation);
        var secret = issued.ApiKey[(DeviceApiKey.Prefix.Length + 33)..];

        var rowAsText = await _factory.ExecuteDbAsync(db =>
            db.Database.SqlQuery<string>($"SELECT row_to_json(k)::text AS \"Value\" FROM device_api_keys k").SingleAsync());

        Assert.DoesNotContain(secret, rowAsText);
        Assert.DoesNotContain(issued.ApiKey, rowAsText);
        Assert.Contains("key_hash", rowAsText);
    }

    [Fact]
    public async Task Revoked_key_is_401_and_the_key_can_be_renewed()
    {
        var (installation, inverter) = await SeedAsync();
        var first = await _factory.IssueKeyAsync(installation);
        using var admin = Admin();

        using var revoke = await admin.DeleteAsync(DeviceKeyTestApi.KeysUrl(installation, $"/{first.Id}"));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = await revoke.ReadJsonAsync<DeviceApiKeyResponse>();
        Assert.False(revoked.Active);
        Assert.NotNull(revoked.RevokedAt);

        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(first.ApiKey, installation, inverter));

        // Renewal after revocation: a new key (issue or rotate) works, the old one stays dead.
        var renewed = await _factory.IssueKeyAsync(installation);
        Assert.NotEqual(first.ApiKey, renewed.ApiKey);
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(renewed.ApiKey, installation, inverter));
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(first.ApiKey, installation, inverter));
    }

    [Fact]
    public async Task Revoking_twice_is_409()
    {
        var (installation, _) = await SeedAsync();
        var key = await _factory.IssueKeyAsync(installation);
        using var admin = Admin();
        (await admin.DeleteAsync(DeviceKeyTestApi.KeysUrl(installation, $"/{key.Id}"))).EnsureSuccessStatusCode();

        using var again = await admin.DeleteAsync(DeviceKeyTestApi.KeysUrl(installation, $"/{key.Id}"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Rotation_replaces_the_key()
    {
        var (installation, inverter) = await SeedAsync();
        var old = await _factory.IssueKeyAsync(installation);
        using var admin = Admin();

        using var response = await admin.PostAsync(DeviceKeyTestApi.KeysUrl(installation, "/rotate"), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rotated = await response.ReadJsonAsync<IssuedDeviceApiKeyResponse>();
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(old.ApiKey, installation, inverter));
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(rotated.ApiKey, installation, inverter));

        var keys = await admin.GetJsonAsync<List<DeviceApiKeyResponse>>(DeviceKeyTestApi.KeysUrl(installation));
        Assert.Equal(2, keys.Count);
        Assert.Single(keys, k => k.Active);
    }

    [Fact]
    public async Task Rotation_without_an_existing_key_issues_one()
    {
        var (installation, inverter) = await SeedAsync();
        using var admin = Admin();

        using var response = await admin.PostAsync(DeviceKeyTestApi.KeysUrl(installation, "/rotate"), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var issued = await response.ReadJsonAsync<IssuedDeviceApiKeyResponse>();
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(issued.ApiKey, installation, inverter));
    }

    [Fact]
    public async Task List_returns_metadata_without_the_key()
    {
        var (installation, _) = await SeedAsync();
        var issued = await _factory.IssueKeyAsync(installation);
        using var admin = Admin();

        using var response = await admin.GetAsync(DeviceKeyTestApi.KeysUrl(installation));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(issued.Id.ToString(), body);
        Assert.DoesNotContain(issued.ApiKey, body);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_installation_or_key_is_404()
    {
        var (installation, _) = await SeedAsync();
        using var admin = Admin();

        using var list = await admin.GetAsync(DeviceKeyTestApi.KeysUrl(Guid.CreateVersion7()));
        using var issue = await admin.PostAsync(DeviceKeyTestApi.KeysUrl(Guid.CreateVersion7()), content: null);
        using var revoke = await admin.DeleteAsync(DeviceKeyTestApi.KeysUrl(installation, $"/{Guid.CreateVersion7()}"));

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, issue.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_key_through_another_installation_is_404()
    {
        var (installation, _) = await SeedAsync();
        var (other, _) = await SeedAsync();
        var key = await _factory.IssueKeyAsync(installation);
        using var admin = Admin();

        using var response = await admin.DeleteAsync(DeviceKeyTestApi.KeysUrl(other, $"/{key.Id}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Management_endpoints_require_login()
    {
        var (installation, _) = await SeedAsync();
        using var anonymous = _factory.CreateApiClient();

        using var list = await anonymous.GetAsync(DeviceKeyTestApi.KeysUrl(installation));
        using var issue = await anonymous.PostAsync(DeviceKeyTestApi.KeysUrl(installation), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, issue.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Sales)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Management_endpoints_are_admin_only(string role)
    {
        var (installation, _) = await SeedAsync();
        var key = await _factory.IssueKeyAsync(installation);
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var list = await client.GetAsync(DeviceKeyTestApi.KeysUrl(installation));
        using var issue = await client.PostAsync(DeviceKeyTestApi.KeysUrl(installation), content: null);
        using var rotate = await client.PostAsync(DeviceKeyTestApi.KeysUrl(installation, "/rotate"), content: null);
        using var revoke = await client.DeleteAsync(DeviceKeyTestApi.KeysUrl(installation, $"/{key.Id}"));

        Assert.All([list, issue, rotate, revoke], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task Database_allows_only_one_active_key_per_installation()
    {
        var (installation, _) = await SeedAsync();
        await _factory.IssueKeyAsync(installation);

        var active = await _factory.ExecuteDbAsync(db =>
            db.Set<DeviceApiKey>().CountAsync(k => k.InstallationId == installation && k.RevokedAt == null));

        Assert.Equal(1, active);
    }
}

public sealed class DeviceApiKeyUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issued_key_parses_and_matches_only_its_own_secret()
    {
        var (key, plaintext) = DeviceApiKey.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        Assert.True(DeviceApiKey.TryParse(plaintext, out var id, out var secret));
        Assert.Equal(key.Id, id);
        Assert.True(key.Matches(secret));
        Assert.False(key.Matches(secret + "x"));
        Assert.DoesNotContain(secret, key.KeyHash);
    }

    [Fact]
    public void Keys_are_unique_and_long()
    {
        var a = DeviceApiKey.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Now).Plaintext;
        var b = DeviceApiKey.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Now).Plaintext;

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 70);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sok_")]
    [InlineData("sok_abc_def")]
    [InlineData("xyz_00000000000000000000000000000000_secret")]
    [InlineData("sok_00000000000000000000000000000000_")]
    public void Malformed_keys_do_not_parse(string? apiKey) => Assert.False(DeviceApiKey.TryParse(apiKey, out _, out _));

    [Fact]
    public void Revoked_key_cannot_be_revoked_again()
    {
        var (key, _) = DeviceApiKey.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        key.Revoke(Guid.CreateVersion7(), Now);

        Assert.False(key.IsActive);
        Assert.Throws<SolarOps.Api.Infrastructure.Errors.DomainException>(() => key.Revoke(Guid.CreateVersion7(), Now));
    }
}
