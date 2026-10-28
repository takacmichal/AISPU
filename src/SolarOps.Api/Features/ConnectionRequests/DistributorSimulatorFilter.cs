using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>
/// Hides the distributor simulator (#87) where it is switched off: <c>ConnectionRequest:Simulator:Enabled</c>, by default
/// on everywhere except Production. A disabled simulator answers 404 as if the endpoint did not exist.
/// </summary>
internal sealed class DistributorSimulatorFilter(IOptions<ConnectionRequestOptions> options, IHostEnvironment environment) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var enabled = options.Value.Simulator.Enabled ?? !environment.IsProduction();
        return enabled
            ? next(context)
            : throw new NotFoundException("The distributor simulator is not available in this environment.");
    }
}
