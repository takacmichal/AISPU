using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Customers;

public sealed class CustomerUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private static CustomerDetails Person(string lastName = "Novák") => new(
        CustomerType.Person, "Ján", lastName, null, null, null, null, "jan@example.sk", null,
        new PostalAddress("Hlavná 12", "Bratislava", "811 01"), null);

    [Fact]
    public void Update_ReplacesDataLinksAccountAndTouchesUpdatedAt()
    {
        var customer = Customer.Create(Person(), null, Now);
        var userId = Guid.CreateVersion7();

        customer.Update(Person("Kováč"), userId, Now.AddDays(1));

        Assert.Equal("Kováč", customer.LastName);
        Assert.Equal("Kováč Ján", customer.SortName);
        Assert.Equal(userId, customer.UserId);
        Assert.Equal(Now.AddDays(1), customer.UpdatedAt);
        Assert.Equal(Now, customer.CreatedAt);
    }

    [Fact]
    public void Update_ArchivedCustomer_Throws()
    {
        var customer = Customer.Create(Person(), null, Now);
        customer.Archive(Now);

        Assert.Throws<DomainException>(() => customer.Update(Person("Kováč"), null, Now));
        Assert.Equal("Novák", customer.LastName);
    }

    [Fact]
    public void Update_InvalidData_ThrowsAndKeepsOldValues()
    {
        var customer = Customer.Create(Person(), null, Now);

        Assert.Throws<RequestValidationException>(() => customer.Update(Person(lastName: " "), null, Now));

        Assert.Equal("Novák", customer.LastName);
    }

    [Fact]
    public void EnsureActive_ThrowsOnlyForArchivedCustomer()
    {
        var customer = Customer.Create(Person(), null, Now);
        customer.EnsureActive();

        customer.Archive(Now);

        Assert.Throws<DomainException>(customer.EnsureActive);
    }

    [Fact]
    public void ArchiveAndReactivate_RejectRedundantTransitions()
    {
        var customer = Customer.Create(Person(), null, Now);
        Assert.Throws<DomainException>(() => customer.Reactivate(Now));

        customer.Archive(Now);
        Assert.Throws<DomainException>(() => customer.Archive(Now));

        customer.Reactivate(Now.AddHours(1));
        Assert.Equal(CustomerStatus.Active, customer.Status);
    }
}
