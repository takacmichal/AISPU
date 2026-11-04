using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Installations;

/// <summary>
/// Whether the distributor approved the connection of an installation: some connection request of it reached
/// <see cref="ConnectionRequestState.ContractOffered"/> ("schválená", ADR #217) or a later state. Planning (#88) and
/// commissioning (#92) depend on it. A rejected request can be followed by a new one, so any approved request counts.
/// </summary>
internal static class ConnectionApproval
{
    private static readonly ConnectionRequestState[] ApprovedStates =
    [
        ConnectionRequestState.ContractOffered,
        ConnectionRequestState.ContractSigned,
        ConnectionRequestState.ContractSubmitted,
        ConnectionRequestState.Concluded,
    ];

    public static Task<bool> IsApprovedAsync(AppDbContext db, Guid installationId, CancellationToken ct) =>
        db.Set<ConnectionRequest>().AsNoTracking().AnyAsync(x => x.InstallationId == installationId && ApprovedStates.Contains(x.State), ct);
}
