using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Installations;

public sealed class PanelStringTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static InstallationDevice NewDevice(DeviceType type = DeviceType.Panel, string serial = "PAN-1") =>
        InstallationDevice.Register(Guid.CreateVersion7(), type, serial, "JA Solar", "JAM54", 450, User, Now);

    [Fact]
    public void Create_TrimsName_AndKeepsNumberAndMppt()
    {
        var s = PanelString.Create(Guid.CreateVersion7(), 2, "  South roof ", 1, Now);

        Assert.Equal(2, s.Number);
        Assert.Equal("South roof", s.Name);
        Assert.Equal(1, s.MpptInput);
        Assert.Equal(Now, s.CreatedAt);
    }

    [Fact]
    public void Create_BlankName_BecomesNull()
    {
        Assert.Null(PanelString.Create(Guid.CreateVersion7(), 1, "  ", null, Now).Name);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 25)]
    public void Create_OutOfRange_ThrowsValidation(int number, int mppt)
    {
        Assert.Throws<RequestValidationException>(() => PanelString.Create(Guid.CreateVersion7(), number, null, mppt, Now));
    }

    [Fact]
    public void AssignToString_Panel_SetsStringId_AndMovesBetweenStrings()
    {
        var panel = NewDevice();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        panel.AssignToString(first);
        panel.AssignToString(second);

        Assert.Equal(second, panel.StringId);
    }

    [Fact]
    public void AssignToString_Inverter_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => NewDevice(DeviceType.Inverter, "INV-1").AssignToString(Guid.CreateVersion7()));
    }

    [Fact]
    public void AssignToString_ReplacedPanel_ThrowsDomainException()
    {
        var panel = NewDevice();
        panel.Replace("PAN-2", "Longi", "LR5", 460, "Defect", User, Now);

        Assert.Throws<DomainException>(() => panel.AssignToString(Guid.CreateVersion7()));
    }

    [Fact]
    public void Replace_MovesStringPlaceToReplacement()
    {
        var panel = NewDevice();
        var stringId = Guid.CreateVersion7();
        panel.AssignToString(stringId);

        var replacement = panel.Replace("PAN-2", "Longi", "LR5", 460, "Defect", User, Now);

        Assert.Null(panel.StringId);
        Assert.Equal(stringId, replacement.StringId);
    }

    [Fact]
    public void RemoveFromString_ClearsStringId()
    {
        var panel = NewDevice();
        panel.AssignToString(Guid.CreateVersion7());

        panel.RemoveFromString();

        Assert.Null(panel.StringId);
    }
}
