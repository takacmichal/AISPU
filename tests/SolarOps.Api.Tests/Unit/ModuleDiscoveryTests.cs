using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Features.System;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Tests.Unit;

public sealed class ModuleDiscoveryTests
{
    [Fact]
    public void DiscoverModules_FindsSystemModule()
    {
        var modules = ModuleExtensions.DiscoverModules(typeof(IModule).Assembly);

        Assert.Contains(modules, m => m is SystemModule);
    }

    [Fact]
    public void DiscoverModules_IsDeterministicAndDistinct()
    {
        var first = ModuleExtensions.DiscoverModules(typeof(IModule).Assembly).Select(m => m.GetType()).ToList();
        var second = ModuleExtensions.DiscoverModules(typeof(IModule).Assembly).Select(m => m.GetType()).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public void AddModules_RegistersEveryModuleAsIModule()
    {
        var services = new ServiceCollection();

        services.AddModules(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<IModule>(), m => m is SystemModule);
    }
}
