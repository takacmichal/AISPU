using Scalar.AspNetCore;

namespace SolarOps.Api.Infrastructure.Documentation;

public static class ApiDocumentationExtensions
{
    public const string EnabledKey = "OpenApi:Enabled";

    /// <summary>
    /// Maps the OpenAPI document (<c>/openapi/v1.json</c>) and the Scalar UI (<c>/scalar/v1</c>). Always on in
    /// Development; elsewhere only when <c>OpenApi:Enabled</c> is true. Both are anonymous on purpose: the document
    /// holds no secrets and the Scalar UI cannot send a bearer token.
    /// </summary>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>(EnabledKey))
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        return app;
    }
}
