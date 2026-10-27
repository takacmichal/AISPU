using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Installations;

public interface IDeviceService
{
    Task<IReadOnlyList<DeviceResponse>> ListAsync(Guid installationId, DeviceType? type, bool includeReplaced, CancellationToken ct);

    Task<DeviceResponse> GetAsync(Guid installationId, Guid deviceId, CancellationToken ct);

    Task<DeviceResponse> AddAsync(Guid installationId, AddDeviceRequest request, CancellationToken ct);

    Task<DeviceReplacementResponse> ReplaceAsync(Guid installationId, Guid deviceId, ReplaceDeviceRequest request, CancellationToken ct);

    /// <summary>The whole replacement chain the device belongs to, oldest first.</summary>
    Task<IReadOnlyList<DeviceResponse>> HistoryAsync(Guid installationId, Guid deviceId, CancellationToken ct);
}

internal sealed class DeviceService(AppDbContext db, ICurrentUser currentUser, ICustomerScope customerScope, TimeProvider time) : IDeviceService
{
    public async Task<IReadOnlyList<DeviceResponse>> ListAsync(Guid installationId, DeviceType? type, bool includeReplaced, CancellationToken ct)
    {
        await EnsureVisibleAsync(installationId, ct);

        var devices = db.Set<InstallationDevice>().AsNoTracking().Where(d => d.InstallationId == installationId);
        if (type is { } t)
        {
            devices = devices.Where(d => d.Type == t);
        }

        if (!includeReplaced)
        {
            devices = devices.Where(d => d.Status == DeviceStatus.Installed);
        }

        var result = await devices.OrderBy(d => d.Type).ThenBy(d => d.RegisteredAt).ThenBy(d => d.Id).ToListAsync(ct);
        return result.ConvertAll(DeviceResponse.From);
    }

    public async Task<DeviceResponse> GetAsync(Guid installationId, Guid deviceId, CancellationToken ct)
    {
        await EnsureVisibleAsync(installationId, ct);
        var device = await db.Set<InstallationDevice>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.InstallationId == installationId, ct)
            ?? throw new NotFoundException("Device", deviceId);
        return DeviceResponse.From(device);
    }

    public async Task<DeviceResponse> AddAsync(Guid installationId, AddDeviceRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await EnsureVisibleAsync(installationId, ct);
        if (installation.IsClosed)
        {
            throw new DomainException("Installation has been handed over; new devices can no longer be added (use replacement).");
        }

        await EnsureSerialFreeAsync(request.SerialNumber!, ct);

        var device = InstallationDevice.Register(
            installationId, request.Type!.Value, request.SerialNumber!, request.Manufacturer!, request.Model!, request.PowerW!.Value,
            userId, time.GetUtcNow());

        db.Add(device);
        await SaveAsync(ct);
        return DeviceResponse.From(device);
    }

    public async Task<DeviceReplacementResponse> ReplaceAsync(Guid installationId, Guid deviceId, ReplaceDeviceRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(installationId, ct);

        var old = await db.Set<InstallationDevice>()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.InstallationId == installationId, ct)
            ?? throw new NotFoundException("Device", deviceId);

        await EnsureSerialFreeAsync(request.SerialNumber!, ct);

        var replacement = old.Replace(
            request.SerialNumber!, request.Manufacturer!, request.Model!, request.PowerW!.Value, request.Reason!,
            userId, time.GetUtcNow());

        db.Add(replacement);
        await SaveAsync(ct);
        return new DeviceReplacementResponse(DeviceResponse.From(old), DeviceResponse.From(replacement));
    }

    public async Task<IReadOnlyList<DeviceResponse>> HistoryAsync(Guid installationId, Guid deviceId, CancellationToken ct)
    {
        await EnsureVisibleAsync(installationId, ct);

        // A chain never leaves its installation, so one query is enough to walk it in memory.
        var devices = await db.Set<InstallationDevice>().AsNoTracking()
            .Where(d => d.InstallationId == installationId)
            .ToDictionaryAsync(d => d.Id, ct);
        if (!devices.TryGetValue(deviceId, out var current))
        {
            throw new NotFoundException("Device", deviceId);
        }

        while (current.ReplacesDeviceId is { } previous && devices.TryGetValue(previous, out var predecessor))
        {
            current = predecessor;
        }

        var chain = new List<DeviceResponse> { DeviceResponse.From(current) };
        while (current.ReplacedByDeviceId is { } next && devices.TryGetValue(next, out var successor))
        {
            chain.Add(DeviceResponse.From(successor));
            current = successor;
        }

        return chain;
    }

    // Another customer's installation looks exactly like a missing one.
    private async Task<Installation> EnsureVisibleAsync(Guid installationId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(x => x.CustomerId == own);
        }

        return await visible.FirstOrDefaultAsync(x => x.Id == installationId, ct)
            ?? throw new NotFoundException("Installation", installationId);
    }

    private async Task EnsureSerialFreeAsync(string serialNumber, CancellationToken ct)
    {
        var serial = InstallationDevice.NormalizeSerial(serialNumber);
        if (await db.Set<InstallationDevice>().AnyAsync(d => d.SerialNumber == serial, ct))
        {
            throw new DomainException($"Serial number '{serial}' is already registered.");
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(InstallationDeviceConfiguration.SerialIndexName))
        {
            // Race on the unique serial index (the pre-check above handles the common case).
            throw new DomainException("The serial number is already registered.");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(InstallationDeviceConfiguration.ReplacesIndexName))
        {
            throw new DomainException("The device has already been replaced.");
        }
    }
}
