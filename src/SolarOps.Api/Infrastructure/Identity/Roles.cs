namespace SolarOps.Api.Infrastructure.Identity;

/// <summary>Application roles (spike #207). Use these constants instead of string literals.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Sales = "Sales";
    public const string Designer = "Designer";
    public const string Technician = "Technician";
    public const string Operator = "Operator";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Admin, Sales, Designer, Technician, Operator, Customer];
}
