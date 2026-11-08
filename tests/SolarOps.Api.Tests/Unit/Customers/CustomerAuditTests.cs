using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Customers;

/// <summary>Audit trail and GDPR anonymization of a customer (#140).</summary>
public sealed class CustomerAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private static CustomerDetails Person(string lastName = "Novák") => new(
        CustomerType.Person, "Ján", lastName, null, null, null, null, "jan.novak@example.sk", "+421 900 123 456",
        new PostalAddress("Hlavná 12", "Bratislava", "811 01"), "Poznámka");

    [Fact]
    public void Create_RecordsWhoAndWhen()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);

        var entry = Assert.Single(customer.AuditEntries);
        Assert.Equal(CustomerAction.Created, entry.Action);
        Assert.Equal(Admin, entry.PerformedBy);
        Assert.Equal(Now, entry.PerformedAt);
    }

    [Fact]
    public void Update_RecordsChangedFields_WithoutPersonalValues()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);
        var accountId = Guid.CreateVersion7();

        customer.Update(Person("Tajná"), accountId, Admin, Now.AddHours(1));

        var entry = customer.AuditEntries[^1];
        Assert.Equal(CustomerAction.Updated, entry.Action);
        Assert.Equal(Admin, entry.PerformedBy);
        Assert.Equal(Now.AddHours(1), entry.PerformedAt);
        Assert.Contains(nameof(Customer.LastName), entry.Details);
        Assert.Contains(nameof(Customer.UserId), entry.Details);
        Assert.DoesNotContain("Tajná", entry.Details);
        Assert.DoesNotContain("jan.novak@example.sk", entry.Details);
    }

    [Fact]
    public void Update_WithSameData_RecordsNoChangedFields()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);

        customer.Update(Person(), null, Admin, Now.AddHours(1));

        Assert.Equal("No changes", customer.AuditEntries[^1].Details);
    }

    [Fact]
    public void ArchiveAndReactivate_RecordStateTransitions()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);

        customer.Archive(Admin, Now.AddHours(1));
        customer.Reactivate(Admin, Now.AddHours(2));

        Assert.Equal([CustomerAction.Created, CustomerAction.Archived, CustomerAction.Reactivated], customer.AuditEntries.Select(e => e.Action));
        Assert.Equal("Status Active -> Archived", customer.AuditEntries[1].Details);
        Assert.Equal("Status Archived -> Active", customer.AuditEntries[2].Details);
    }

    [Fact]
    public void Anonymize_ErasesPersonalDataAndArchivesTheCustomer()
    {
        var userId = Guid.CreateVersion7();
        var customer = Customer.Create(Person(), userId, Admin, Now);

        customer.Anonymize(Admin, Now.AddDays(1));

        Assert.Null(customer.FirstName);
        Assert.Null(customer.LastName);
        Assert.Null(customer.CompanyName);
        Assert.Null(customer.Ico);
        Assert.Null(customer.Dic);
        Assert.Null(customer.IcDph);
        Assert.Null(customer.Phone);
        Assert.Null(customer.Note);
        Assert.Null(customer.UserId);
        Assert.Equal(string.Empty, customer.Address.Street);
        Assert.Equal(string.Empty, customer.Address.City);
        Assert.Equal(string.Empty, customer.Address.PostalCode);
        Assert.Equal("Anonymizovaný zákazník", customer.DisplayName);
        Assert.Equal(CustomerStatus.Archived, customer.Status);
        Assert.Equal(Now.AddDays(1), customer.AnonymizedAt);
        Assert.DoesNotContain("jan.novak@example.sk", customer.Email, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("@anonymized.invalid", customer.Email, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymize_KeepsTheAuditTrailAndRecordsTheErasure()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);
        customer.Update(Person("Kováč"), null, Admin, Now.AddHours(1));

        customer.Anonymize(Admin, Now.AddDays(1));

        Assert.Equal(
            [CustomerAction.Created, CustomerAction.Updated, CustomerAction.Anonymized],
            customer.AuditEntries.Select(e => e.Action));
        Assert.DoesNotContain(customer.AuditEntries, e => (e.Details ?? string.Empty).Contains("Novák", StringComparison.Ordinal));
        Assert.DoesNotContain(customer.AuditEntries, e => (e.Details ?? string.Empty).Contains("Kováč", StringComparison.Ordinal));
    }

    [Fact]
    public void Anonymize_Twice_Throws()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);
        customer.Anonymize(Admin, Now.AddDays(1));

        Assert.Throws<DomainException>(() => customer.Anonymize(Admin, Now.AddDays(2)));
    }

    [Fact]
    public void AnonymizedCustomer_CannotBeEditedOrReactivated()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);
        customer.Anonymize(Admin, Now.AddDays(1));

        Assert.Throws<DomainException>(() => customer.Update(Person(), null, Admin, Now.AddDays(2)));
        Assert.Throws<DomainException>(() => customer.Reactivate(Admin, Now.AddDays(2)));
        Assert.Throws<DomainException>(customer.EnsureActive);
    }

    [Fact]
    public void EveryAuditEntry_HasActorAndTimestamp()
    {
        var customer = Customer.Create(Person(), null, Admin, Now);
        customer.Update(Person("Kováč"), null, Admin, Now.AddHours(1));
        customer.Archive(Admin, Now.AddHours(2));
        customer.Anonymize(Admin, Now.AddHours(3));

        Assert.All(customer.AuditEntries, entry =>
        {
            Assert.NotEqual(Guid.Empty, entry.PerformedBy);
            Assert.True(entry.PerformedAt >= Now);
        });
    }
}
