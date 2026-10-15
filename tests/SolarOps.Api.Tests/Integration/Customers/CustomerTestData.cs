using SolarOps.Api.Features.Customers;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Customers;

/// <summary>Valid customer requests and helpers shared by customer-related integration tests.</summary>
public static class CustomerTestData
{
    /// <summary>Real IČO with a valid check digit (Slovak Telekom).</summary>
    public const string ValidIco = "35763469";

    public static AddressDto Address() => new()
    {
        Street = "Hlavná 12",
        City = "Bratislava",
        PostalCode = "811 01",
    };

    public static CreateCustomerRequest Person(string email = "jan.novak@example.sk", string lastName = "Novák", string firstName = "Ján") => new()
    {
        Type = CustomerType.Person,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        Phone = "+421 900 123 456",
        Address = Address(),
    };

    public static CreateCustomerRequest Company(string email = "info@slnko.sk", string companyName = "Slnko s.r.o.") => new()
    {
        Type = CustomerType.Company,
        CompanyName = companyName,
        Ico = ValidIco,
        Dic = "2020273893",
        IcDph = "SK2020273893",
        Email = email,
        Address = Address(),
    };

    public static async Task<CustomerResponse> CreateAsync(HttpClient client, CreateCustomerRequest request)
    {
        using var response = await client.PostJsonAsync("/api/customers", request);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<CustomerResponse>();
    }
}
