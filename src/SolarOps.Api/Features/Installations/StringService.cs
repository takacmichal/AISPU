using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Installations;

public interface IStringService
{
    Task<StringLayoutResponse> GetLayoutAsync(Guid installationId, CancellationToken ct);

    Task<PanelStringResponse> GetAsync(Guid installationId, Guid stringId, CancellationToken ct);

    Task<PanelStringResponse> CreateAsync(Guid installationId, CreateStringRequest request, CancellationToken ct);

    Task<PanelStringResponse> UpdateAsync(Guid installationId, Guid stringId, UpdateStringRequest request, CancellationToken ct);

    Task DeleteAsync(Guid installationId, Guid stringId, CancellationToken ct);

    /// <summary>Places an installed panel into the string; a panel in another string is moved.</summary>
    Task<PanelStringResponse> AssignPanelAsync(Guid installationId, Guid stringId, Guid deviceId, CancellationToken ct);

    Task UnassignPanelAsync(Guid installationId, Guid stringId, Guid deviceId, CancellationToken ct);
}

internal sealed class StringService(AppDbContext db, ICustomerScope customerScope, TimeProvider time) : IStringService
{
    public async Task<StringLayoutResponse> GetLayoutAsync(Guid installationId, CancellationToken ct)
    {
        await EnsureVisibleAsync(installationId, ct);

        var strings = await db.Set<PanelString>().AsNoTracking()
            .Where(s => s.InstallationId == installationId)
            .OrderBy(s => s.Number)
            .ToListAsync(ct);
        var panels = await InstalledPanels(installationId).AsNoTracking().ToListAsync(ct);

        var byString = panels.Where(p => p.StringId is not null).ToLookup(p => p.StringId!.Value);
        var responses = strings.ConvertAll(s => PanelStringResponse.From(s, byString[s.Id]));
        var unassigned = panels.Where(p => p.StringId is null)
            .OrderBy(p => p.RegisteredAt).ThenBy(p => p.Id)
            .Select(StringPanelResponse.From).ToList();

        return new StringLayoutResponse(
            installationId, responses, unassigned, responses.Sum(s => s.TotalPowerW), panels.Sum(p => (long)p.PowerW));
    }

    public async Task<PanelStringResponse> GetAsync(Guid installationId, Guid stringId, CancellationToken ct)
    {
        await EnsureVisibleAsync(installationId, ct);
        var panelString = await FindAsync(installationId, stringId, track: false, ct);
        return await ToResponseAsync(panelString, ct);
    }

    public async Task<PanelStringResponse> CreateAsync(Guid installationId, CreateStringRequest request, CancellationToken ct)
    {
        await EnsureEditableAsync(installationId, ct);
        await EnsureNumberFreeAsync(installationId, request.Number!.Value, exceptId: null, ct);

        var panelString = PanelString.Create(installationId, request.Number!.Value, request.Name, request.MpptInput, time.GetUtcNow());
        db.Add(panelString);
        await SaveAsync(ct);
        return PanelStringResponse.From(panelString, []);
    }

    public async Task<PanelStringResponse> UpdateAsync(Guid installationId, Guid stringId, UpdateStringRequest request, CancellationToken ct)
    {
        await EnsureEditableAsync(installationId, ct);
        var panelString = await FindAsync(installationId, stringId, track: true, ct);
        await EnsureNumberFreeAsync(installationId, request.Number!.Value, stringId, ct);

        panelString.Update(request.Number!.Value, request.Name, request.MpptInput);
        await SaveAsync(ct);
        return await ToResponseAsync(panelString, ct);
    }

    public async Task DeleteAsync(Guid installationId, Guid stringId, CancellationToken ct)
    {
        await EnsureEditableAsync(installationId, ct);
        var panelString = await FindAsync(installationId, stringId, track: true, ct);

        if (await db.Set<InstallationDevice>().AnyAsync(d => d.StringId == stringId, ct))
        {
            throw new DomainException($"String {panelString.Number} still has panels; remove them from the string first.");
        }

        db.Remove(panelString);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PanelStringResponse> AssignPanelAsync(Guid installationId, Guid stringId, Guid deviceId, CancellationToken ct)
    {
        await EnsureEditableAsync(installationId, ct);
        var panelString = await FindAsync(installationId, stringId, track: false, ct);
        var device = await FindPanelAsync(installationId, deviceId, ct);

        device.AssignToString(stringId);
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(panelString, ct);
    }

    public async Task UnassignPanelAsync(Guid installationId, Guid stringId, Guid deviceId, CancellationToken ct)
    {
        await EnsureEditableAsync(installationId, ct);
        await FindAsync(installationId, stringId, track: false, ct);
        var device = await FindPanelAsync(installationId, deviceId, ct);
        if (device.StringId != stringId)
        {
            throw new NotFoundException("Panel in string", deviceId);
        }

        device.RemoveFromString();
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<InstallationDevice> InstalledPanels(Guid installationId) =>
        db.Set<InstallationDevice>().Where(d =>
            d.InstallationId == installationId && d.Type == DeviceType.Panel && d.Status == DeviceStatus.Installed);

    private async Task<PanelStringResponse> ToResponseAsync(PanelString panelString, CancellationToken ct)
    {
        var panels = await InstalledPanels(panelString.InstallationId).AsNoTracking()
            .Where(d => d.StringId == panelString.Id).ToListAsync(ct);
        return PanelStringResponse.From(panelString, panels);
    }

    private async Task<PanelString> FindAsync(Guid installationId, Guid stringId, bool track, CancellationToken ct)
    {
        var query = db.Set<PanelString>().Where(s => s.Id == stringId && s.InstallationId == installationId);
        return await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("String", stringId);
    }

    private async Task<InstallationDevice> FindPanelAsync(Guid installationId, Guid deviceId, CancellationToken ct) =>
        await db.Set<InstallationDevice>()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.InstallationId == installationId && d.Type == DeviceType.Panel, ct)
        ?? throw new NotFoundException("Panel", deviceId);

    private async Task EnsureNumberFreeAsync(Guid installationId, int number, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Set<PanelString>().AnyAsync(s => s.InstallationId == installationId && s.Number == number && s.Id != exceptId, ct))
        {
            throw new DomainException($"String number {number} is already used in this installation.");
        }
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

    private async Task EnsureEditableAsync(Guid installationId, CancellationToken ct)
    {
        var installation = await EnsureVisibleAsync(installationId, ct);
        if (installation.IsClosed)
        {
            throw new DomainException("Installation has been handed over; the string layout can no longer be changed.");
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(PanelStringConfiguration.NumberIndexName))
        {
            // Race on the unique (installation, number) index; the pre-check handles the common case.
            throw new DomainException("The string number is already used in this installation.");
        }
    }
}
