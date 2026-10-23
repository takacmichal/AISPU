using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit;

public sealed class ApiExceptionHandlerTests
{
    private static async Task<(bool Handled, HttpContext Context, JsonElement? Body)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<ApiExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.Body.Position = 0;
        JsonElement? body = context.Response.Body.Length > 0
            ? (await JsonDocument.ParseAsync(context.Response.Body)).RootElement
            : null;
        return (handled, context, body);
    }

    [Fact]
    public async Task DomainException_Returns409ProblemDetails()
    {
        var (handled, context, body) = await HandleAsync(new DomainException("Invalid state transition."));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.Equal(409, body!.Value.GetProperty("status").GetInt32());
        Assert.Equal("Invalid state transition.", body.Value.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NotFoundException_Returns404ProblemDetails()
    {
        var (handled, context, body) = await HandleAsync(new NotFoundException("Project", Guid.Empty));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(404, body!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task RequestValidationException_Returns400WithErrors()
    {
        var (handled, context, body) = await HandleAsync(new RequestValidationException("Name", "Name is required."));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Name is required.", body!.Value.GetProperty("errors").GetProperty("Name")[0].GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns401ProblemDetails()
    {
        var (handled, context, body) = await HandleAsync(new UnauthorizedAccessException("User is not authenticated."));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(401, body!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UnknownException_IsNotHandled()
    {
        var (handled, _, _) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.False(handled);
    }
}
