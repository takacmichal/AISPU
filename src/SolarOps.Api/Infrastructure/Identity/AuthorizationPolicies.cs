using Microsoft.AspNetCore.Authorization;

namespace SolarOps.Api.Infrastructure.Identity;

/// <summary>
/// Authorization policies of the API (#136). Every endpoint either opts out with <c>AllowAnonymous</c> or requires
/// one of these policies; the role sets are the source of truth for the role × endpoint matrix in
/// <c>docs/security/authorization.md</c>. Modules must not spell out role names on endpoints.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Administration: users, roles, the last administrator guard.</summary>
    public const string AdminOnly = "AdminOnly";

    public const string CustomersRead = "CustomersRead";
    public const string CustomersWrite = "CustomersWrite";

    /// <summary>Panel, inverter and labor catalog (#73).</summary>
    public const string CatalogRead = "CatalogRead";
    public const string CatalogWrite = "CatalogWrite";

    public const string QuotesRead = "QuotesRead";
    public const string QuotesWrite = "QuotesWrite";

    public const string ContractsRead = "ContractsRead";
    public const string ContractsWrite = "ContractsWrite";

    /// <summary>Technical PV design (#76 to #78); write is the BPMN lane Projektant.</summary>
    public const string DesignsRead = "DesignsRead";
    public const string DesignsWrite = "DesignsWrite";

    public const string InstallationsRead = "InstallationsRead";

    /// <summary>Planning an installation: create, edit, schedule (BPMN lane Obchod/Operátor).</summary>
    public const string InstallationsPlan = "InstallationsPlan";

    /// <summary>Field work: state transitions, devices and serial numbers.</summary>
    public const string InstallationsField = "InstallationsField";

    public const string ConnectionRequestsRead = "ConnectionRequestsRead";
    public const string ConnectionRequestsWrite = "ConnectionRequestsWrite";

    /// <summary>
    /// Policy name → roles allowed by the policy. Read policies include every role that may see the data;
    /// a customer still gets only its own records through <c>ICustomerScope</c> (#137).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Matrix =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AdminOnly] = [Roles.Admin],

            [CustomersRead] = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Operator],
            [CustomersWrite] = [Roles.Admin, Roles.Sales],

            [CatalogRead] = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator],
            [CatalogWrite] = [Roles.Admin, Roles.Sales],

            [QuotesRead] = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator],
            [QuotesWrite] = [Roles.Admin, Roles.Sales],

            [ContractsRead] = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator],
            [ContractsWrite] = [Roles.Admin, Roles.Sales],

            [DesignsRead] = [Roles.Admin, Roles.Sales, Roles.Designer],
            [DesignsWrite] = [Roles.Admin, Roles.Designer],

            [InstallationsRead] = [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Customer],
            [InstallationsPlan] = [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer],
            [InstallationsField] = [Roles.Admin, Roles.Operator, Roles.Designer, Roles.Technician],

            [ConnectionRequestsRead] = [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Customer],
            [ConnectionRequestsWrite] = [Roles.Admin, Roles.Operator, Roles.Sales],
        };

    /// <summary>Names of all policies; used by tests to prove that every policy is documented and exercised.</summary>
    public static readonly IReadOnlyCollection<string> All = Matrix.Keys.ToArray();

    /// <summary>
    /// Registers the policies. There is deliberately no fallback policy: it would also answer unmatched routes,
    /// which must stay 404. Endpoints are protected by explicit policies and the endpoint tests fail when a new
    /// endpoint forgets one (#136).
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        foreach (var (name, roles) in Matrix)
        {
            options.AddPolicy(name, policy => policy.RequireRole(roles));
        }
    }
}
