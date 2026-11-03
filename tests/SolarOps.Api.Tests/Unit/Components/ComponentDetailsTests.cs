using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Components;

public sealed class ComponentDetailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private static ComponentDetails Panel(
        string code = "PAN-1", string name = "Panel", string? manufacturer = "Acme", int? power = 450, decimal price = 100m) =>
        new(ComponentType.Panel, code, name, manufacturer, power, price);

    [Fact]
    public void Valid_Panel_HasNoErrors() => Assert.Empty(Panel().Validate());

    [Fact]
    public void Valid_Labor_WithoutPower_HasNoErrors() =>
        Assert.Empty(new ComponentDetails(ComponentType.Labor, "WORK", "Montáž", null, null, 45m).Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Code_Blank_IsRejected(string code) => Assert.Contains("Code", Panel(code: code).Validate().Keys);

    [Fact]
    public void Code_AtMaxLength_IsValid_AndOneOverIsRejected()
    {
        Assert.Empty(Panel(code: new string('A', CatalogComponent.CodeMaxLength)).Validate());
        Assert.Contains("Code", Panel(code: new string('A', CatalogComponent.CodeMaxLength + 1)).Validate().Keys);
    }

    [Fact]
    public void Name_Blank_IsRejected() => Assert.Contains("Name", Panel(name: " ").Validate().Keys);

    [Fact]
    public void Name_TooLong_IsRejected() =>
        Assert.Contains("Name", Panel(name: new string('n', CatalogComponent.NameMaxLength + 1)).Validate().Keys);

    [Fact]
    public void Manufacturer_TooLong_IsRejected() =>
        Assert.Contains("Manufacturer", Panel(manufacturer: new string('m', CatalogComponent.ManufacturerMaxLength + 1)).Validate().Keys);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void PanelAndInverter_NeedPositivePower(int? power)
    {
        Assert.Contains("PowerW", Panel(power: power).Validate().Keys);
        Assert.Contains("PowerW", new ComponentDetails(ComponentType.Inverter, "INV", "Inv", null, power, 10m).Validate().Keys);
    }

    [Fact]
    public void Labor_WithPower_IsRejected() =>
        Assert.Contains("PowerW", new ComponentDetails(ComponentType.Labor, "WORK", "Montáž", null, 100, 45m).Validate().Keys);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1_000_000.01)]
    public void UnitPrice_OutOfRange_IsRejected(double price) =>
        Assert.Contains("UnitPrice", Panel(price: (decimal)price).Validate().Keys);

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    [InlineData(99.99)]
    public void UnitPrice_OnBoundary_IsValid(double price) => Assert.Empty(Panel(price: (decimal)price).Validate());

    [Fact]
    public void UnitPrice_WithThreeDecimals_IsRejected() =>
        Assert.Contains("UnitPrice", Panel(price: 10.005m).Validate().Keys);

    [Fact]
    public void Create_TrimsNameAndManufacturer()
    {
        var component = CatalogComponent.Create(Panel(name: "  Panel X ", manufacturer: " Acme "), Now);

        Assert.Equal("Panel X", component.Name);
        Assert.Equal("Acme", component.Manufacturer);
    }

    [Fact]
    public void Deactivate_Twice_Throws()
    {
        var component = CatalogComponent.Create(Panel(), Now);
        component.Deactivate(Now.AddDays(1));

        Assert.False(component.IsActive);
        Assert.Equal(Now.AddDays(1), component.UpdatedAt);
        Assert.Throws<DomainException>(() => component.Deactivate(Now));
    }

    [Fact]
    public void Activate_ActiveComponent_Throws()
    {
        var component = CatalogComponent.Create(Panel(), Now);

        Assert.Throws<DomainException>(() => component.Activate(Now));
    }

    [Fact]
    public void Activate_AfterDeactivate_MakesComponentActiveAgain()
    {
        var component = CatalogComponent.Create(Panel(), Now);
        component.Deactivate(Now);

        component.Activate(Now.AddHours(1));

        Assert.True(component.IsActive);
        component.EnsureActive();
    }

    [Fact]
    public void Update_InvalidDetails_ThrowsAndKeepsOldValues()
    {
        var component = CatalogComponent.Create(Panel(price: 100m), Now);

        Assert.Throws<RequestValidationException>(() => component.Update(Panel(price: -1m), Now));

        Assert.Equal(100m, component.UnitPrice);
    }

    [Theory]
    [InlineData(" pan-1 ", "PAN-1")]
    [InlineData("abc", "ABC")]
    public void NormalizeCode_TrimsAndUpperCases(string code, string expected) =>
        Assert.Equal(expected, CatalogComponent.NormalizeCode(code));

    [Fact]
    public void Request_MissingRequiredMembers_ReportsThemAndNothingElse()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateComponentRequest());

        Assert.Contains(nameof(CreateComponentRequest.Type), errors.Keys);
        Assert.Contains(nameof(CreateComponentRequest.Code), errors.Keys);
        Assert.Contains(nameof(CreateComponentRequest.Name), errors.Keys);
        Assert.Contains(nameof(CreateComponentRequest.UnitPrice), errors.Keys);
    }

    [Fact]
    public void Request_DomainRuleViolation_IsReportedPerField()
    {
        var request = new CreateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = "PAN",
            Name = "Panel",
            UnitPrice = 10m,
        };

        Assert.Contains("PowerW", DataAnnotationsValidator.Validate(request).Keys);
    }
}
