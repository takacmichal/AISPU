using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.ConnectionRequests;

public interface IConnectionRequestService
{
    Task<ConnectionRequestResponse> CreateAsync(CreateConnectionRequestRequest request, CancellationToken ct);

    Task<ConnectionRequestResponse> GetAsync(Guid id, CancellationToken ct);
}

internal sealed class ConnectionRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    IOptions<ConnectionRequestOptions> options,
    TimeProvider time) : IConnectionRequestService
{
    public async Task<ConnectionRequestResponse> CreateAsync(CreateConnectionRequestRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installationId = request.InstallationId!.Value;

        // Customer and supply point come from the installation, so the request cannot point at a foreign supply point.
        var installation = await db.Set<Installation>().AsNoTracking()
            .Where(x => x.Id == installationId)
            .Select(x => new { x.CustomerId, x.SupplyPointId })
            .FirstOrDefaultAsync(ct)
            ?? throw new RequestValidationException(nameof(CreateConnectionRequestRequest.InstallationId), "Installation does not exist.");

        var attachments = request.Attachments!
            .Select(a => new AttachmentInput(a.Kind!.Value, a.FileName!, a.ContentType!, a.SizeBytes))
            .ToList();

        var connectionRequest = ConnectionRequest.Create(
            installationId,
            installation.CustomerId,
            installation.SupplyPointId,
            request.EicCode!,
            request.RatedAcPowerKw,
            options.Value.SmallSourceMaxKw,
            request.MainBreakerAmps,
            attachments,
            userId,
            time.GetUtcNow());

        db.Add(connectionRequest);
        await db.SaveChangesAsync(ct);
        return ConnectionRequestResponse.From(connectionRequest);
    }

    public async Task<ConnectionRequestResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<ConnectionRequest>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(x => x.CustomerId == own);
        }

        // Another customer's request looks exactly like a missing one.
        var connectionRequest = await visible
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Connection request", id);

        return ConnectionRequestResponse.From(connectionRequest);
    }
}
