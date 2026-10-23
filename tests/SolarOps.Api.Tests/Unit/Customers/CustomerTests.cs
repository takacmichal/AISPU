using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Customers;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static CustomerDetails Person(string email = "Jan.Novak@Example.sk") => new(
        CustomerType.Person, " Ján ", " Novák ", "ignored", null, null, null, email, null,
        new PostalAddress("Hlavná 12", "Bratislava", "811 01"), null);

    private static CustomerDetails Company(string? ico = "35763469") => new(
        CustomerType.Company, null, null, "Slnko s.r.o.", ico, null, null, "info@slnko.sk", null,
        new PostalAddress("Hlavná 12", "Bratislava", "81101"), null);

    [Fact]
    public void Create_Person_NormalizesDataAndSortsBySurname()
    {
        var customer = Customer.Create(Person(), null, Now);

        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal("jan.novak@example.sk", customer.Email);
        Assert.Equal("Ján Novák", customer.DisplayName);
        Assert.Equal("Novák Ján", customer.SortName);
        Assert.Null(customer.CompanyName);
        Assert.Equal("SK", customer.Address.Country);
        Assert.Equal(Now, customer.CreatedAt);
    }

    [Fact]
    public void Create_Company_UsesCompanyNameAsSortName()
    {
        var customer = Customer.Create(Company(), null, Now);

        Assert.Equal("Slnko s.r.o.", customer.SortName);
        Assert.Equal("Slnko s.r.o.", customer.DisplayName);
    }

    [Fact]
    public void Create_CompanyWithoutIco_Throws()
    {
        var ex = Assert.Throws<RequestValidationException>(() => Customer.Create(Company(ico: null), null, Now));
        Assert.Contains(nameof(Customer.Ico), ex.Errors.Keys);
    }

    [Fact]
    public void Create_CompanyWithInvalidIcoChecksum_Throws()
    {
        var ex = Assert.Throws<RequestValidationException>(() => Customer.Create(Company(ico: "35763468"), null, Now));
        Assert.Contains(nameof(Customer.Ico), ex.Errors.Keys);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("")]
    public void Validate_RejectsInvalidEmail(string email)
    {
        var errors = Person(email).Normalize().Validate();
        Assert.Contains(nameof(CustomerDetails.Email), errors.Keys);
    }

    [Fact]
    public void Validate_RejectsInvalidSlovakPostalCode()
    {
        var details = Person() with { Address = new PostalAddress("Hlavná 1", "Bratislava", "8110") };
        Assert.Contains("Address.PostalCode", details.Normalize().Validate().Keys);
    }

    [Fact]
    public void Archive_ThenReactivate_ChangesStatus()
    {
        var customer = Customer.Create(Person(), null, Now);

        customer.Archive(Now.AddDays(1));
        Assert.Equal(CustomerStatus.Archived, customer.Status);
        Assert.Throws<DomainException>(() => customer.Archive(Now));
        Assert.Throws<DomainException>(() => customer.Update(Person(), null, Now));

        customer.Reactivate(Now.AddDays(2));
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Throws<DomainException>(() => customer.Reactivate(Now));
    }
}
