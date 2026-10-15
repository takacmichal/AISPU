namespace SolarOps.Api.Infrastructure.Modules;

/// <summary>
/// A vertical slice (feature module). Every non-abstract implementation with a public
/// parameterless constructor in this assembly is discovered automatically at startup,
/// so feature PRs never need to edit Program.cs.
/// </summary>
public interface IModule
{
    /// <summary>Registers the module's services (called before the app is built).</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints (called after the middleware pipeline is set up).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
