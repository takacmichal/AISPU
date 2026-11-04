using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Tickets;

internal sealed class ServiceTicketAssignmentConfiguration : IEntityTypeConfiguration<ServiceTicketAssignment>
{
    public void Configure(EntityTypeBuilder<ServiceTicketAssignment> builder)
    {
        builder.ToTable("service_ticket_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.ServiceTicketId).IsUnique();
        builder.HasIndex(x => x.TechnicianId);
        builder.HasOne<ServiceTicket>().WithMany().HasForeignKey(x => x.ServiceTicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceVisitConfiguration : IEntityTypeConfiguration<ServiceVisit>
{
    public void Configure(EntityTypeBuilder<ServiceVisit> builder)
    {
        builder.ToTable("service_visits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Cause).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Findings).HasMaxLength(ServiceVisit.TextMaxLength);
        builder.Property(x => x.WorkDone).HasMaxLength(ServiceVisit.TextMaxLength);

        builder.HasIndex(x => x.ServiceTicketId);
        builder.HasIndex(x => new { x.TechnicianId, x.PlannedStart });

        builder.HasOne<ServiceTicket>().WithMany().HasForeignKey(x => x.ServiceTicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Reschedules).WithOne().HasForeignKey(x => x.ServiceVisitId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Reschedules).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.IsActive);
    }
}

internal sealed class ServiceVisitRescheduleConfiguration : IEntityTypeConfiguration<ServiceVisitReschedule>
{
    public void Configure(EntityTypeBuilder<ServiceVisitReschedule> builder)
    {
        builder.ToTable("service_visit_reschedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(ServiceVisit.ReasonMaxLength).IsRequired();
        builder.HasIndex(x => new { x.ServiceVisitId, x.ChangedAt });
    }
}

internal sealed class ServiceVisitMaterialConfiguration : IEntityTypeConfiguration<ServiceVisitMaterial>
{
    public void Configure(EntityTypeBuilder<ServiceVisitMaterial> builder)
    {
        builder.ToTable("service_visit_materials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(ServiceVisitMaterial.NameMaxLength).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(ServiceVisitMaterial.UnitMaxLength).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(12, 3);
        builder.HasIndex(x => x.ServiceVisitId);
        builder.HasIndex(x => x.ServiceTicketId);
        builder.HasOne<ServiceVisit>().WithMany().HasForeignKey(x => x.ServiceVisitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ServiceTicket>().WithMany().HasForeignKey(x => x.ServiceTicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceVisitDeviceReplacementConfiguration : IEntityTypeConfiguration<ServiceVisitDeviceReplacement>
{
    public void Configure(EntityTypeBuilder<ServiceVisitDeviceReplacement> builder)
    {
        builder.ToTable("service_visit_device_replacements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(ServiceVisit.ReasonMaxLength).IsRequired();
        builder.HasIndex(x => x.ServiceVisitId);
        builder.HasIndex(x => x.ServiceTicketId);

        // Devices live in the Installations module: referenced by id only.
        builder.HasOne<ServiceVisit>().WithMany().HasForeignKey(x => x.ServiceVisitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ServiceTicket>().WithMany().HasForeignKey(x => x.ServiceTicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceTicketConfirmationConfiguration : IEntityTypeConfiguration<ServiceTicketConfirmation>
{
    public void Configure(EntityTypeBuilder<ServiceTicketConfirmation> builder)
    {
        builder.ToTable("service_ticket_confirmations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Comment).HasMaxLength(ServiceTicketConfirmation.CommentMaxLength);
        builder.HasIndex(x => new { x.ServiceTicketId, x.DecidedAt });
        builder.HasOne<ServiceTicket>().WithMany().HasForeignKey(x => x.ServiceTicketId).OnDelete(DeleteBehavior.Cascade);
    }
}
