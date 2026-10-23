using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Installations;

/// <summary>Lifecycle of an installation (BPMN #65, #66; mapping #69).</summary>
public enum InstallationState
{
    Planned,
    ComponentsOrdered,
    ComponentsReceived,
    Mounting,
    MountingDone,
    Inspection,
    InspectionFailed,
    InspectionPassed,
    Commissioned,
    HandedOver,
}

/// <summary>
/// A PV plant being built for a customer. Bound to a contract and a supply point of other modules by id only.
/// State changes go through <see cref="TransitionTo"/> (or the named shortcuts) which reject invalid transitions.
/// </summary>
public sealed class Installation
{
    public const int NameMaxLength = 200;
    public const int NoteMaxLength = 2000;

    private static readonly IReadOnlyDictionary<InstallationState, InstallationState[]> AllowedTransitionMap =
        new Dictionary<InstallationState, InstallationState[]>
        {
            [InstallationState.Planned] = [InstallationState.ComponentsOrdered],
            [InstallationState.ComponentsOrdered] = [InstallationState.ComponentsReceived],
            [InstallationState.ComponentsReceived] = [InstallationState.Mounting],
            [InstallationState.Mounting] = [InstallationState.MountingDone],
            [InstallationState.MountingDone] = [InstallationState.Inspection],
            [InstallationState.Inspection] = [InstallationState.InspectionPassed, InstallationState.InspectionFailed],
            // Rework after a failed inspection returns the installation to mounting.
            [InstallationState.InspectionFailed] = [InstallationState.Mounting],
            [InstallationState.InspectionPassed] = [InstallationState.Commissioned],
            [InstallationState.Commissioned] = [InstallationState.HandedOver],
            [InstallationState.HandedOver] = [],
        };

    private readonly List<InstallationStateChange> _stateChanges = [];

    private Installation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid SupplyPointId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Planned DC peak power (kWp) from the design; the actual value is derived from devices.</summary>
    public decimal? PlannedPowerKwp { get; private set; }

    public string? Note { get; private set; }

    public InstallationState State { get; private set; }

    public DateOnly? ScheduledStart { get; private set; }

    public DateOnly? ScheduledEnd { get; private set; }

    /// <summary>User ids of technicians assigned to the installation.</summary>
    public List<Guid> TechnicianIds { get; private set; } = [];

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset StateChangedAt { get; private set; }

    public IReadOnlyList<InstallationStateChange> StateChanges => _stateChanges;

    public IReadOnlyList<InstallationState> AllowedTransitions => AllowedTransitionMap[State];

    public bool IsClosed => State == InstallationState.HandedOver;

    public static Installation Create(
        Guid customerId,
        Guid contractId,
        Guid supplyPointId,
        string name,
        decimal? plannedPowerKwp,
        string? note,
        Guid createdBy,
        DateTimeOffset now)
    {
        var installation = new Installation
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            ContractId = contractId,
            SupplyPointId = supplyPointId,
            Name = name.Trim(),
            PlannedPowerKwp = plannedPowerKwp,
            Note = note,
            State = InstallationState.Planned,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            StateChangedAt = now,
        };
        installation._stateChanges.Add(InstallationStateChange.Create(installation.Id, null, InstallationState.Planned, createdBy, null, now));
        return installation;
    }

    public static bool CanTransition(InstallationState from, InstallationState to) => AllowedTransitionMap[from].Contains(to);

    public void UpdateDetails(string name, decimal? plannedPowerKwp, string? note, DateTimeOffset now)
    {
        EnsureNotClosed();
        Name = name.Trim();
        PlannedPowerKwp = plannedPowerKwp;
        Note = note;
        UpdatedAt = now;
    }

    public void Schedule(DateOnly start, DateOnly end, DateTimeOffset now)
    {
        if (end < start)
        {
            throw new DomainException("Scheduled end must not be before scheduled start.");
        }

        if (State is InstallationState.Commissioned or InstallationState.HandedOver)
        {
            throw new DomainException($"Installation in state {State} can no longer be rescheduled.");
        }

        ScheduledStart = start;
        ScheduledEnd = end;
        UpdatedAt = now;
    }

    public void AssignTechnicians(IEnumerable<Guid> technicianIds, DateTimeOffset now)
    {
        EnsureNotClosed();
        TechnicianIds = technicianIds.Distinct().ToList();
        UpdatedAt = now;
    }

    /// <summary>Moves the installation to <paramref name="target"/>; throws <see cref="DomainException"/> (409) when not allowed.</summary>
    public void TransitionTo(InstallationState target, Guid changedBy, string? note, DateTimeOffset now)
    {
        if (!CanTransition(State, target))
        {
            throw new DomainException($"Installation cannot move from {State} to {target}.");
        }

        if (target == InstallationState.Mounting && (ScheduledStart is null || TechnicianIds.Count == 0))
        {
            throw new DomainException("Mounting can start only when the installation is scheduled and has at least one technician.");
        }

        var from = State;
        State = target;
        StateChangedAt = now;
        UpdatedAt = now;
        _stateChanges.Add(InstallationStateChange.Create(Id, from, target, changedBy, note, now));
    }

    public void OrderComponents(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.ComponentsOrdered, by, null, now);

    public void ReceiveComponents(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.ComponentsReceived, by, null, now);

    public void StartMounting(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.Mounting, by, null, now);

    public void CompleteMounting(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.MountingDone, by, null, now);

    public void StartInspection(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.Inspection, by, null, now);

    public void RecordInspection(bool passed, Guid by, string? note, DateTimeOffset now) =>
        TransitionTo(passed ? InstallationState.InspectionPassed : InstallationState.InspectionFailed, by, note, now);

    public void Rework(Guid by, string? note, DateTimeOffset now) => TransitionTo(InstallationState.Mounting, by, note, now);

    public void Commission(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.Commissioned, by, null, now);

    public void HandOver(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.HandedOver, by, null, now);

    private void EnsureNotClosed()
    {
        if (IsClosed)
        {
            throw new DomainException("Installation has been handed over and can no longer be changed.");
        }
    }
}

/// <summary>Audit record of one state change of an installation (#207: state_transitions).</summary>
public sealed class InstallationStateChange
{
    private InstallationStateChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    public InstallationState? FromState { get; private set; }

    public InstallationState ToState { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }

    internal static InstallationStateChange Create(
        Guid installationId, InstallationState? from, InstallationState to, Guid changedBy, string? note, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        InstallationId = installationId,
        FromState = from,
        ToState = to,
        ChangedBy = changedBy,
        ChangedAt = at,
        Note = note,
    };
}
