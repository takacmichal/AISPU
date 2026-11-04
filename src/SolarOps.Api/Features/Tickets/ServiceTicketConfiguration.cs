using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Tickets;

internal sealed class ServiceTicketConfiguration : IEntityTypeConfiguration<ServiceTicket>
{
    public void Configure(EntityTypeBuilder<ServiceTicket> builder)
    {
        builder.ToTable("service_tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(ServiceTicket.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ServiceTicket.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32);

        // Optimistic concurrency, so two operators cannot move the same ticket at once.
        builder.Property<uint>("xmin").IsRowVersion();

        // Installation and customer live in other modules and are referenced by id only.
        builder.HasIndex(x => x.InstallationId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => new { x.State, x.Priority });
        builder.HasIndex(x => x.CreatedAt);

        builder.HasMany(x => x.StateChanges)
            .WithOne()
            .HasForeignKey(x => x.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.StateChanges).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(x => x.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.AllowedTransitions);
        builder.Ignore(x => x.ClosedAt);
    }
}

internal sealed class ServiceTicketStateChangeConfiguration : IEntityTypeConfiguration<ServiceTicketStateChange>
{
    public void Configure(EntityTypeBuilder<ServiceTicketStateChange> builder)
    {
        builder.ToTable("service_ticket_state_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Note).HasMaxLength(ServiceTicket.NoteMaxLength);
        builder.HasIndex(x => new { x.ServiceTicketId, x.ChangedAt });
    }
}

internal sealed class ServiceTicketCommentConfiguration : IEntityTypeConfiguration<ServiceTicketComment>
{
    public void Configure(EntityTypeBuilder<ServiceTicketComment> builder)
    {
        builder.ToTable("service_ticket_comments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Body).HasMaxLength(ServiceTicket.CommentMaxLength).IsRequired();
        builder.HasIndex(x => new { x.ServiceTicketId, x.CreatedAt });

        // Comments are added without loading the ticket, but still cannot outlive it.
        builder.HasOne<ServiceTicket>()
            .WithMany()
            .HasForeignKey(x => x.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceTicketAttachmentConfiguration : IEntityTypeConfiguration<ServiceTicketAttachment>
{
    public void Configure(EntityTypeBuilder<ServiceTicketAttachment> builder)
    {
        builder.ToTable("service_ticket_attachments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileName).HasMaxLength(ServiceTicket.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(ServiceTicket.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.HasIndex(x => x.ServiceTicketId);
    }
}
