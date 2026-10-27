using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Installations;

public sealed class InstallationDeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(30);
    private static readonly Guid User = Guid.CreateVersion7();

    private static InstallationDevice NewPanel(string serial = " abc-123 ") =>
        InstallationDevice.Register(Guid.CreateVersion7(), DeviceType.Panel, serial, " JA Solar ", " JAM54S30 ", 450, User, Now);

    [Fact]
    public void Register_NormalizesSerial_AndStartsInstalled()
    {
        var device = NewPanel();

        Assert.Equal("ABC-123", device.SerialNumber);
        Assert.Equal("JA Solar", device.Manufacturer);
        Assert.Equal("JAM54S30", device.Model);
        Assert.Equal(450, device.PowerW);
        Assert.Equal(DeviceStatus.Installed, device.Status);
        Assert.Equal(Now, device.RegisteredAt);
        Assert.Equal(User, device.RegisteredBy);
        Assert.Null(device.ReplacedByDeviceId);
        Assert.Null(device.ReplacesDeviceId);
    }

    [Theory]
    [InlineData("", "M", "X", 100)]
    [InlineData("  ", "M", "X", 100)]
    [InlineData("S1", "", "X", 100)]
    [InlineData("S1", "M", " ", 100)]
    [InlineData("S1", "M", "X", 0)]
    [InlineData("S1", "M", "X", -5)]
    public void Register_InvalidInput_ThrowsValidation(string serial, string manufacturer, string model, int powerW)
    {
        Assert.Throws<RequestValidationException>(() =>
            InstallationDevice.Register(Guid.CreateVersion7(), DeviceType.Inverter, serial, manufacturer, model, powerW, User, Now));
    }

    [Fact]
    public void Register_TooLongSerial_ThrowsValidation()
    {
        var serial = new string('A', InstallationDevice.SerialNumberMaxLength + 1);

        Assert.Throws<RequestValidationException>(() => NewPanel(serial));
    }

    [Fact]
    public void Replace_MarksOldAsReplaced_AndLinksNewDevice()
    {
        var old = NewPanel();

        var replacement = old.Replace("xyz-999", "Longi", "LR5-54", 460, " Defective cell ", User, Later);

        Assert.Equal(DeviceStatus.Replaced, old.Status);
        Assert.Equal(Later, old.RemovedAt);
        Assert.Equal(User, old.RemovedBy);
        Assert.Equal("Defective cell", old.RemovalReason);
        Assert.Equal(replacement.Id, old.ReplacedByDeviceId);

        Assert.Equal(DeviceStatus.Installed, replacement.Status);
        Assert.Equal(old.Id, replacement.ReplacesDeviceId);
        Assert.Equal(old.InstallationId, replacement.InstallationId);
        Assert.Equal(old.Type, replacement.Type);
        Assert.Equal("XYZ-999", replacement.SerialNumber);
        Assert.Equal(Later, replacement.RegisteredAt);
    }

    [Fact]
    public void Replace_AlreadyReplacedDevice_ThrowsDomainException()
    {
        var old = NewPanel();
        old.Replace("S2", "M", "X", 400, "Broken", User, Later);

        Assert.Throws<DomainException>(() => old.Replace("S3", "M", "X", 400, "Broken again", User, Later));
    }

    [Fact]
    public void Replace_WithoutReason_ThrowsValidation_AndKeepsDevice()
    {
        var old = NewPanel();

        Assert.Throws<RequestValidationException>(() => old.Replace("S2", "M", "X", 400, " ", User, Later));
        Assert.Equal(DeviceStatus.Installed, old.Status);
    }

    [Fact]
    public void Replace_InvalidNewData_KeepsOldDeviceInstalled()
    {
        var old = NewPanel();

        Assert.Throws<RequestValidationException>(() => old.Replace("", "M", "X", 400, "Broken", User, Later));
        Assert.Equal(DeviceStatus.Installed, old.Status);
        Assert.Null(old.ReplacedByDeviceId);
    }
}
