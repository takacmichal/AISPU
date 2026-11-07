using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Tests.Unit.Tickets;

public sealed class VisitMaterialValidationTests
{
    [Theory]
    [InlineData("sk-SK", "0.001", true)]
    [InlineData("sk-SK", "0.0009", false)]
    [InlineData("sk-SK", "100000", true)]
    [InlineData("sk-SK", "100001", false)]
    public void Quantity_UsesSameBoundsRegardlessOfCulture(string culture, string quantity, bool expectedValid)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var request = new VisitMaterialRequest
            {
                Name = "Cable",
                Quantity = decimal.Parse(quantity, CultureInfo.InvariantCulture),
            };
            var results = new List<ValidationResult>();

            var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

            Assert.Equal(expectedValid, valid);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
