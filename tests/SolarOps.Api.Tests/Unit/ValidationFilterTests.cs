using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit;

public sealed class ValidationFilterTests
{
    private sealed record SampleRequest
    {
        [Required, MaxLength(5)]
        public string? Name { get; init; }

        [Range(1, 10)]
        public int Count { get; init; }
    }

    [Fact]
    public void Validate_ValidObject_ReturnsNoErrors()
    {
        var errors = DataAnnotationsValidator.Validate(new SampleRequest { Name = "abc", Count = 3 });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidObject_GroupsErrorsByMember()
    {
        var errors = DataAnnotationsValidator.Validate(new SampleRequest { Name = null, Count = 42 });

        Assert.Contains("Name", errors.Keys);
        Assert.Contains("Count", errors.Keys);
    }
}
