using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.DeviceKeys;

public interface IDeviceApiKeyService
{
    Task<IReadOnlyList<DeviceApiKeyResponse>> ListAsync(Guid installationId, CancellationToken ct);

    /// <summary>Issues the first key; 409 when the installation already has an active key (use rotate).</summary>
    Task<IssuedDeviceApiKeyResponse> IssueAsync(Guid installationId, CancellationToken ct);

    /// <summary>Revokes the active key (if any) and issues a new one in one transaction.</summary>
    Task<IssuedDeviceApiKeyResponse> RotateAsync(Guid installationId, CancellationToken ct);

    /// <summary>Revokes a key; 404 for an unknown key, 409 when it is already revoked.</summary>
    Task<DeviceApiKeyResponse> RevokeAsync(Guid installationId, Guid keyId, CancellationToken ct);
}

internal sealed class DeviceApiKeyService(AppDbContext db, ICurrentUser currentUser, TimeProvider time) : IDeviceApiKeyService
{
    public async Task<IReadOnlyList<DeviceApiKeyResponse>> ListAsync(Guid installationId, CancellationToken ct)
    {
        await EnsureInstallationAsync(installationId, ct);
        var keys = await db.Set<DeviceApiKey>().AsNoTracking()
            .Where(k => k.InstallationId == installationId)
            .OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id)
            .ToListAsync(ct);
        return keys.ConvertAll(DeviceApiKeyResponse.From);
    }

    public async Task<IssuedDeviceApiKeyResponse> IssueAsync(Guid installationId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await EnsureInstallationAsync(installationId, ct);

        if (await db.Set<DeviceApiKey>().AnyAsync(k => k.InstallationId == installationId && k.RevokedAt == null, ct))
        {
            throw new DomainException("The installation already has an active API key; rotate it to get a new one.");
        }

        return await SaveNewKeyAsync(installationId, userId, ct);
    }

    public async Task<IssuedDeviceApiKeyResponse> RotateAsync(Guid installationId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await EnsureInstallationAsync(installationId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var active = await db.Set<DeviceApiKey>().Where(k => k.InstallationId == installationId && k.RevokedAt == null).ToListAsync(ct);
        foreach (var key in active)
        {
            key.Revoke(userId, time.GetUtcNow());
        }

        // Revocation first: the unique index allows a single active key per installation.
        await SaveAsync(ct);
        var issued = await SaveNewKeyAsync(installationId, userId, ct);
        await tx.CommitAsync(ct);
        return issued;
    }

    public async Task<DeviceApiKeyResponse> RevokeAsync(Guid installationId, Guid keyId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var key = await db.Set<DeviceApiKey>().FirstOrDefaultAsync(k => k.Id == keyId && k.InstallationId == installationId, ct)
            ?? throw new NotFoundException("API key", keyId);

        key.Revoke(userId, time.GetUtcNow());
        await SaveAsync(ct);
        return DeviceApiKeyResponse.From(key);
    }

    private async Task<IssuedDeviceApiKeyResponse> SaveNewKeyAsync(Guid installationId, Guid userId, CancellationToken ct)
    {
        var (key, plaintext) = DeviceApiKey.Issue(installationId, userId, time.GetUtcNow());
        db.Add(key);
        await SaveAsync(ct);
        return new IssuedDeviceApiKeyResponse(key.Id, key.InstallationId, plaintext, key.CreatedAt);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(DeviceApiKeyConfiguration.ActiveKeyIndexName))
        {
            throw new DomainException("The installation already has an active API key (concurrent change).");
        }
    }

    private async Task EnsureInstallationAsync(Guid installationId, CancellationToken ct)
    {
        if (!await db.Set<Installation>().AnyAsync(i => i.Id == installationId, ct))
        {
            throw new NotFoundException("Installation", installationId);
        }
    }
}
