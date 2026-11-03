using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Installations;

internal sealed class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        builder.ToTable("installations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(Installation.NameMaxLength).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(Installation.NoteMaxLength);
        builder.Property(x => x.PlannedPowerKwp).HasPrecision(10, 3);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TechnicianIds).IsRequired();
        builder.Property(x => x.LastRevisionResult).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.HandoverAcceptedBy).HasMaxLength(Installation.AcceptedByMaxLength);
        builder.Property(x => x.CommissioningNote).HasMaxLength(Installation.NoteMaxLength);

        // One installation per contract (#82: installation is bound to a contract and a supply point).
        builder.HasIndex(x => x.ContractId).IsUnique();
        builder.HasIndex(x => x.SupplyPointId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.State);

        builder.Ignore(x => x.AllowedTransitions);
        builder.Ignore(x => x.IsClosed);
        builder.Ignore(x => x.MissingRequiredChecklistSteps);
        builder.Ignore(x => x.IsChecklistComplete);

        builder.HasMany(x => x.StateChanges)
            .WithOne()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.StateChanges).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.ChecklistItems)
            .WithOne()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.ChecklistItems).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InstallationStateChangeConfiguration : IEntityTypeConfiguration<InstallationStateChange>
{
    public void Configure(EntityTypeBuilder<InstallationStateChange> builder)
    {
        builder.ToTable("installation_state_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Note).HasMaxLength(Installation.NoteMaxLength);
        builder.HasIndex(x => new { x.InstallationId, x.ChangedAt });
    }
}

internal sealed class InstallationChecklistItemConfiguration : IEntityTypeConfiguration<InstallationChecklistItem>
{
    public void Configure(EntityTypeBuilder<InstallationChecklistItem> builder)
    {
        builder.ToTable("installation_checklist_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StepCode).HasMaxLength(ChecklistTemplate.CodeMaxLength).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(InstallationChecklistItem.NoteMaxLength);
        builder.HasIndex(x => new { x.InstallationId, x.StepCode }).IsUnique();
    }
}

internal sealed class RevisionReportConfiguration : IEntityTypeConfiguration<RevisionReport>
{
    public void Configure(EntityTypeBuilder<RevisionReport> builder)
    {
        builder.ToTable("installation_revision_reports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.InspectorName).HasMaxLength(RevisionReport.InspectorNameMaxLength).IsRequired();
        builder.Property(x => x.InspectorCertificateNumber).HasMaxLength(RevisionReport.CertificateMaxLength);
        builder.Property(x => x.Result).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Findings).HasMaxLength(RevisionReport.FindingsMaxLength);
        builder.Property(x => x.FileName).HasMaxLength(RevisionReport.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(RevisionReport.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.HasIndex(x => new { x.InstallationId, x.RecordedAt });

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InstallationDocumentDownloadConfiguration : IEntityTypeConfiguration<InstallationDocumentDownload>
{
    public void Configure(EntityTypeBuilder<InstallationDocumentDownload> builder)
    {
        builder.ToTable("installation_document_downloads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.InstallationId, x.DownloadedAt });

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
