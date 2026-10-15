namespace SolarOps.Api.Infrastructure.Errors;

/// <summary>Invalid input detected in a service. Mapped to 400 with a validation problem details body.</summary>
public class ValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IDictionary<string, string[]> Errors { get; } = errors;
}
