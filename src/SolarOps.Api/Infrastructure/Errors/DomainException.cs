namespace SolarOps.Api.Infrastructure.Errors;

/// <summary>
/// Violation of a business rule (e.g. invalid state transition). Mapped to 409 Conflict.
/// The message is returned to the client, so it must be safe and user-facing.
/// </summary>
public class DomainException(string message) : Exception(message);
