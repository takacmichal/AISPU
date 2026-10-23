using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.System;

/// <summary>
/// Reference example of the module pattern: one class implementing <see cref="IModule"/>
/// registers services and maps endpoints of the feature under <c>/api/&lt;module&gt;</c>.
/// </summary>
public sealed class SystemModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Module services go here, e.g. services.AddScoped<IFooService, FooService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/system").WithTags("System");

        group.MapGet("/info", GetInfo)
            .WithName("GetSystemInfo")
            .WithSummary("Get API name, version and environment")
            .AllowAnonymous();
    }

    internal static Ok<SystemInfoResponse> GetInfo(IHostEnvironment environment, TimeProvider timeProvider)
    {
        var assembly = typeof(SystemModule).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        return TypedResults.Ok(new SystemInfoResponse("SolarOps API", version, environment.EnvironmentName, timeProvider.GetUtcNow()));
    }
}

/// <summary>Basic information about the running API.</summary>
public sealed record SystemInfoResponse(string Name, string Version, string Environment, DateTimeOffset ServerTime);
