namespace SolarOps.Api.Infrastructure.Errors;

/// <summary>Requested resource does not exist (or is not visible to the current user). Mapped to 404.</summary>
public class NotFoundException(string message) : Exception(message)
{
    public NotFoundException(string resource, object id)
        : this($"{resource} '{id}' was not found.")
    {
    }
}
