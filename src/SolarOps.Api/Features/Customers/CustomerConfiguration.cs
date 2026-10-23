using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Features.Customers;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.FirstName).HasMaxLength(CustomerDetails.NameMaxLength);
        builder.Property(c => c.LastName).HasMaxLength(CustomerDetails.NameMaxLength);
        builder.Property(c => c.CompanyName).HasMaxLength(CustomerDetails.CompanyNameMaxLength);
        builder.Property(c => c.Ico).HasMaxLength(8);
        builder.Property(c => c.Dic).HasMaxLength(10);
        builder.Property(c => c.IcDph).HasMaxLength(12);
        builder.Property(c => c.Email).HasMaxLength(CustomerDetails.EmailMaxLength);
        builder.Property(c => c.Phone).HasMaxLength(CustomerDetails.PhoneMaxLength);
        builder.Property(c => c.Note).HasMaxLength(CustomerDetails.NoteMaxLength);
        builder.Property(c => c.SortName).HasMaxLength(CustomerDetails.CompanyNameMaxLength + 1);
        builder.Ignore(c => c.DisplayName);

        builder.OwnsOne(c => c.Address, address =>
        {
            address.Property(a => a.Street).HasMaxLength(PostalAddress.StreetMaxLength);
            address.Property(a => a.City).HasMaxLength(PostalAddress.CityMaxLength);
            address.Property(a => a.PostalCode).HasMaxLength(PostalAddress.PostalCodeMaxLength);
            address.Property(a => a.Country).HasMaxLength(2);
        });
        builder.Navigation(c => c.Address).IsRequired();

        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.Ico);
        builder.HasIndex(c => c.SortName);

        // Optional link to the login account; deleting the account keeps the customer.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
