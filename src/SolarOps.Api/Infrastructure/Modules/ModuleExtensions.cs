using System.Reflection;

namespace SolarOps.Api.Infrastructure.Modules;

public static class ModuleExtensions
{
    /// <summary>
    /// Finds all <see cref="IModule"/> implementations in the given assembly,
    /// ordered by full type name so registration order is deterministic.
    /// </summary>
    public static IReadOnlyList<IModule> DiscoverModules(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IModule).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToList();

    /// <summary>
    /// Discovers modules in the API assembly, calls <see cref="IModule.AddServices"/> on each
    /// and registers them as singletons so <see cref="MapModules"/> can map their endpoints.
    /// </summary>
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in DiscoverModules(typeof(IModule).Assembly))
        {
            module.AddServices(services, configuration);
            services.AddSingleton(module);
        }

        return services;
    }

    /// <summary>Maps endpoints of all registered modules.</summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
