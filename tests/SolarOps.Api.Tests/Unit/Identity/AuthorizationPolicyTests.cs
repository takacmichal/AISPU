using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Tests.Unit.Identity;

/// <summary>
/// Guards the single source of truth of authorization (#136): every policy in
/// <see cref="AuthorizationPolicies.Matrix"/> is registered with exactly the documented roles,
/// and the fallback only requires an authenticated user.
/// </summary>
public sealed class AuthorizationPolicyTests
{
    private static AuthorizationOptions ConfiguredOptions()
    {
        var options = new AuthorizationOptions();
        AuthorizationPolicies.Configure(options);
        return options;
    }

    [Fact]
    public void Configure_RegistersEveryPolicyFromTheMatrix()
    {
        var options = ConfiguredOptions();

        Assert.NotEmpty(AuthorizationPolicies.All);
        foreach (var name in AuthorizationPolicies.All)
        {
            Assert.NotNull(options.GetPolicy(name));
        }
    }

    [Fact]
    public void EveryPolicyAllowsExactlyTheRolesFromTheMatrix()
    {
        var options = ConfiguredOptions();

        foreach (var (name, roles) in AuthorizationPolicies.Matrix)
        {
            var requirement = Assert.Single(options.GetPolicy(name)!.Requirements.OfType<RolesAuthorizationRequirement>());
            Assert.Equal(roles.Order(StringComparer.Ordinal), requirement.AllowedRoles.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void EveryPolicyAllowsAtLeastOneKnownRole()
    {
        foreach (var (name, roles) in AuthorizationPolicies.Matrix)
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.NotEmpty(roles);
            Assert.All(roles, role => Assert.Contains(role, Roles.All));
            Assert.Equal(roles.Length, roles.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void PolicyConstants_MatchTheMatrixKeys()
    {
        var constants = typeof(AuthorizationPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: not null } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.Equal(AuthorizationPolicies.All.Order(StringComparer.Ordinal), constants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Configure_LeavesFallbackPolicyUnset_SoUnknownRoutesStay404()
    {
        // A fallback policy would also apply to requests without a matched endpoint, turning 404 into 401.
        Assert.Null(ConfiguredOptions().FallbackPolicy);
    }

    [Fact]
    public void DocumentedMatrix_ListsEveryPolicy()
    {
        var documentation = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "security", "authorization.md"));

        foreach (var policy in AuthorizationPolicies.All)
        {
            Assert.Contains($"`{policy}`", documentation, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SolarOps.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
