using System.Text;
using SolarOps.Api.Features.Installations;

namespace SolarOps.Api.Tests.Unit.Installations;

/// <summary>
/// Walks an installation through its lifecycle the legitimate way (schedule, technicians, checklist, revision report,
/// commissioning), so tests do not depend on the endpoints. Shared by unit and integration tests.
/// </summary>
internal static class InstallationLifecycle
{
    /// <summary>Smallest byte sequence that passes the PDF signature check of revision report files.</summary>
    public static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%test revision report\n%%EOF\n");

    public static RevisionReport NewReport(Installation installation, RevisionResult result, DateTimeOffset now, DateOnly? validUntil = null)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return RevisionReport.Create(
            installation.Id,
            today,
            "Ing. Ján Revízny",
            "REV-123/2026",
            result,
            result == RevisionResult.Passed ? validUntil ?? today.AddYears(4) : validUntil,
            result == RevisionResult.Passed ? null : "Chýba uzemnenie",
            "revizia.pdf",
            "application/pdf",
            PdfBytes,
            Guid.CreateVersion7(),
            now);
    }

    public static void CompleteChecklist(Installation installation, Guid by, DateTimeOffset now)
    {
        foreach (var step in ChecklistTemplate.Steps.Where(s => s.Required))
        {
            installation.SetChecklistStep(step.Code, done: true, note: null, by, now);
        }
    }

    /// <summary>
    /// Moves <paramref name="installation"/> forward to <paramref name="target"/>. Returns the revision reports it created;
    /// they are separate entities, so integration tests must add them to the context.
    /// </summary>
    public static IReadOnlyList<RevisionReport> WalkTo(Installation installation, InstallationState target, DateTimeOffset now)
    {
        var by = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var reports = new List<RevisionReport>();

        if (installation.ScheduledStart is null)
        {
            installation.Schedule(today, today.AddDays(1), now);
        }

        if (installation.TechnicianIds.Count == 0)
        {
            installation.AssignTechnicians([Guid.CreateVersion7()], now);
        }

        while (installation.State != target)
        {
            switch (installation.State)
            {
                case InstallationState.Planned:
                    installation.OrderComponents(by, now);
                    break;
                case InstallationState.ComponentsOrdered:
                    installation.ReceiveComponents(by, now);
                    break;
                case InstallationState.ComponentsReceived:
                case InstallationState.InspectionFailed:
                    installation.StartMounting(by, now);
                    break;
                case InstallationState.Mounting:
                    CompleteChecklist(installation, by, now);
                    installation.CompleteMounting(by, now);
                    break;
                case InstallationState.MountingDone:
                    installation.StartInspection(by, now);
                    break;
                case InstallationState.Inspection:
                    var report = NewReport(installation, target == InstallationState.InspectionFailed ? RevisionResult.Failed : RevisionResult.Passed, now);
                    installation.RecordRevision(report, by, now);
                    reports.Add(report);
                    break;
                case InstallationState.InspectionPassed:
                    installation.Commission(today, "Peter Zákazník", null, connectionApproved: true, by, now);
                    break;
                case InstallationState.Commissioned:
                    installation.HandOver(by, now);
                    break;
                default:
                    throw new InvalidOperationException($"Cannot walk from {installation.State} to {target}.");
            }
        }

        return reports;
    }
}
