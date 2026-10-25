using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Components;

public sealed class CatalogComponentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    private static ComponentDetails Panel(decimal price = 100m) => new(ComponentType.Panel, "pan-1", "Panel", "  ", 450, price);

    [Fact]
    public void Create_NormalizesCodeAndBlankManufacturer_AndIsActive()
    {
        var component = CatalogComponent.Create(Panel(), Now);

        Assert.Equal("PAN-1", component.Code);
        Assert.Null(component.Manufacturer);
        Assert.True(component.IsActive);
        Assert.Equal(Now, component.CreatedAt);
    }

    [Fact]
    public void Update_ChangesPrice_AndTouchesUpdatedAt()
    {
        var component = CatalogComponent.Create(Panel(100m), Now);

        component.Update(Panel(80m), Now.AddDays(1));

        Assert.Equal(80m, component.UnitPrice);
        Assert.Equal(Now.AddDays(1), component.UpdatedAt);
        Assert.Equal(Now, component.CreatedAt);
    }

    [Fact]
    public void EnsureActive_ThrowsForDeactivatedComponent()
    {
        var component = CatalogComponent.Create(Panel(), Now);
        component.EnsureActive();

        component.Deactivate(Now);

        Assert.Throws<DomainException>(component.EnsureActive);
    }

    [Fact]
    public void Create_InvalidDetails_Throws()
    {
        Assert.Throws<RequestValidationException>(() => CatalogComponent.Create(Panel(-5m), Now));
    }
}
