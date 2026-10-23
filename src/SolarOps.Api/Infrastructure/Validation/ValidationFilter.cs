using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Infrastructure.Validation;

/// <summary>
/// Endpoint filter validating the first argument of type <typeparamref name="T"/>
/// with DataAnnotations (including <see cref="IValidatableObject"/>). Returns 400 problem details on failure.
/// Usage: <c>group.MapPost("/", Handler).WithValidation&lt;CreateXRequest&gt;();</c>
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [typeof(T).Name] = ["Request body is required."],
            });
        }

        var errors = DataAnnotationsValidator.Validate(argument, context.HttpContext.RequestServices);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await next(context);
    }
}

public static class DataAnnotationsValidator
{
    /// <summary>Validates an object graph root (top-level properties) and groups errors by member name.</summary>
    public static Dictionary<string, string[]> Validate(object instance, IServiceProvider? services = null)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance, services, null), results, validateAllProperties: true);

        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty])
                .Select(m => (Member: m, Message: r.ErrorMessage ?? "Invalid value.")))
            .GroupBy(x => x.Member, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray(), StringComparer.Ordinal);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Adds DataAnnotations validation of the <typeparamref name="T"/> argument (→ 400).</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
