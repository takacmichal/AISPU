using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Installations;

public sealed class InstallationRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static CreateInstallationRequest Create() => new()
    {
        CustomerId = Guid.CreateVersion7(),
        ContractId = Guid.CreateVersion7(),
        SupplyPointId = Guid.CreateVersion7(),
        Name = "Rodinný dom",
        PlannedPowerKwp = 9.5m,
    };

    private static Installation Planned() =>
        Installation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Dom", null, null, User, Now);

    [Fact]
    public void Create_Valid_HasNoErrors() => Assert.Empty(DataAnnotationsValidator.Validate(Create()));

    [Fact]
    public void Create_MissingMembers_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateInstallationRequest());

        Assert.Contains(nameof(CreateInstallationRequest.CustomerId), errors.Keys);
        Assert.Contains(nameof(CreateInstallationRequest.ContractId), errors.Keys);
        Assert.Contains(nameof(CreateInstallationRequest.SupplyPointId), errors.Keys);
        Assert.Contains(nameof(CreateInstallationRequest.Name), errors.Keys);
    }

    [Fact]
    public void Create_EmptyGuids_AreRejected()
    {
        var errors = DataAnnotationsValidator.Validate(Create() with
        {
            CustomerId = Guid.Empty,
            ContractId = Guid.Empty,
            SupplyPointId = Guid.Empty,
        });

        Assert.Contains(nameof(CreateInstallationRequest.CustomerId), errors.Keys);
        Assert.Contains(nameof(CreateInstallationRequest.ContractId), errors.Keys);
        Assert.Contains(nameof(CreateInstallationRequest.SupplyPointId), errors.Keys);
    }

    [Fact]
    public void Create_BlankName_IsRejected() =>
        Assert.Contains(nameof(CreateInstallationRequest.Name), DataAnnotationsValidator.Validate(Create() with { Name = "   " }).Keys);

    [Fact]
    public void Create_NameAtMax_IsValid_AndOneOverIsRejected()
    {
        Assert.Empty(DataAnnotationsValidator.Validate(Create() with { Name = new string('n', Installation.NameMaxLength) }));
        Assert.Contains(nameof(CreateInstallationRequest.Name),
            DataAnnotationsValidator.Validate(Create() with { Name = new string('n', Installation.NameMaxLength + 1) }).Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void Create_PlannedPowerOutOfRange_IsRejected(double kwp) =>
        Assert.Contains(nameof(CreateInstallationRequest.PlannedPowerKwp),
            DataAnnotationsValidator.Validate(Create() with { PlannedPowerKwp = (decimal)kwp }).Keys);

    [Fact]
    public void Create_TooLongNote_IsRejected() =>
        Assert.Contains(nameof(CreateInstallationRequest.Note),
            DataAnnotationsValidator.Validate(Create() with { Note = new string('n', Installation.NoteMaxLength + 1) }).Keys);

    [Fact]
    public void Update_BlankName_IsRejected() =>
        Assert.Contains(nameof(UpdateInstallationRequest.Name),
            DataAnnotationsValidator.Validate(new UpdateInstallationRequest { Name = " " }).Keys);

    [Fact]
    public void Update_Valid_HasNoErrors() =>
        Assert.Empty(DataAnnotationsValidator.Validate(new UpdateInstallationRequest { Name = "Dom", PlannedPowerKwp = 5m }));

    [Fact]
    public void Schedule_EndBeforeStart_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(new ScheduleInstallationRequest
        {
            ScheduledStart = new DateOnly(2026, 11, 3),
            ScheduledEnd = new DateOnly(2026, 11, 2),
        });

        Assert.Contains(nameof(ScheduleInstallationRequest.ScheduledEnd), errors.Keys);
    }

    [Fact]
    public void Schedule_SingleDayWindow_IsValid()
    {
        var day = new DateOnly(2026, 11, 3);

        Assert.Empty(DataAnnotationsValidator.Validate(new ScheduleInstallationRequest { ScheduledStart = day, ScheduledEnd = day }));
    }

    [Fact]
    public void Schedule_MissingDates_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new ScheduleInstallationRequest());

        Assert.Contains(nameof(ScheduleInstallationRequest.ScheduledStart), errors.Keys);
        Assert.Contains(nameof(ScheduleInstallationRequest.ScheduledEnd), errors.Keys);
    }

    [Fact]
    public void AssignTechnicians_EmptyId_IsRejected() =>
        Assert.Contains(nameof(AssignTechniciansRequest.TechnicianIds),
            DataAnnotationsValidator.Validate(new AssignTechniciansRequest { TechnicianIds = [Guid.CreateVersion7(), Guid.Empty] }).Keys);

    [Fact]
    public void AssignTechnicians_MoreThanTwenty_IsRejected() =>
        Assert.Contains(nameof(AssignTechniciansRequest.TechnicianIds),
            DataAnnotationsValidator.Validate(new AssignTechniciansRequest { TechnicianIds = [.. Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7())] }).Keys);

    [Fact]
    public void AssignTechnicians_EmptyList_IsAllowedToUnassignEveryone() =>
        Assert.Empty(DataAnnotationsValidator.Validate(new AssignTechniciansRequest { TechnicianIds = [] }));

    [Fact]
    public void Transition_MissingTarget_IsRequired() =>
        Assert.Contains(nameof(InstallationTransitionRequest.TargetState),
            DataAnnotationsValidator.Validate(new InstallationTransitionRequest()).Keys);

    [Fact]
    public void UpdateDetails_TrimsNameAndTouchesUpdatedAt()
    {
        var installation = Planned();

        installation.UpdateDetails("  Chata ", 7.2m, "poznámka", Now.AddDays(1));

        Assert.Equal("Chata", installation.Name);
        Assert.Equal(7.2m, installation.PlannedPowerKwp);
        Assert.Equal("poznámka", installation.Note);
        Assert.Equal(Now.AddDays(1), installation.UpdatedAt);
    }

    [Fact]
    public void AssignTechnicians_RemovesDuplicates()
    {
        var installation = Planned();
        var technician = Guid.CreateVersion7();

        installation.AssignTechnicians([technician, technician], Now);

        Assert.Equal([technician], installation.TechnicianIds);
    }

    [Fact]
    public void Schedule_EndBeforeStart_ThrowsDomainException() =>
        Assert.Throws<DomainException>(() => Planned().Schedule(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 2), Now));
}
