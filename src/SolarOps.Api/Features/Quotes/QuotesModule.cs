using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Quotes;

/// <summary>Quotes built from the component catalog (#74) with a status workflow (#75): <c>/api/quotes</c>.</summary>
public sealed class QuotesModule : IModule
{
    internal static readonly string[] ReadRoles = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator];

    internal static readonly string[] WriteRoles = [Roles.Admin, Roles.Sales];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QuoteOptions>().Bind(configuration.GetSection(QuoteOptions.SectionName));
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddHostedService<QuoteExpirationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/quotes").WithTags("Quotes");

        group.MapPost("/", CreateAsync)
            .WithName("CreateQuote")
            .WithSummary("Create a quote (state Draft) from catalog components")
            .WithDescription("Send customerId and lines (componentId, quantity). Prices are taken from the catalog and copied into the quote; "
                + "net, VAT and gross totals are computed by the server. Inactive components and archived customers give 409.")
            .WithValidation<CreateQuoteRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetQuote")
            .WithSummary("Get a quote with its lines and totals")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapPost("/{id:guid}/send", SendAsync)
            .WithName("SendQuote")
            .WithSummary("Send the quote to the customer (Draft to Sent)")
            .WithDescription("Optional body: validityDays (1 to 365, default 30) and note. The quote expires when the validity ends.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapPost("/{id:guid}/accept", AcceptAsync)
            .WithName("AcceptQuote")
            .WithSummary("Record that the customer accepted the quote (Sent to Accepted)")
            .WithDescription("A quote past its validity is refused with 409 and marked Expired.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapPost("/{id:guid}/reject", RejectAsync)
            .WithName("RejectQuote")
            .WithSummary("Record that the customer rejected the quote (Sent to Rejected)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}/history", HistoryAsync)
            .WithName("GetQuoteHistory")
            .WithSummary("Status change history of the quote, oldest first")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/", ListAsync)
            .WithName("ListQuotes")
            .WithSummary("List quotes")
            .WithDescription("Filters: customerId, status. Newest first.")
            .ProducesValidationProblem()
            .RequireAuthorization(p => p.RequireRole(ReadRoles));
    }

    internal static async Task<Created<QuoteResponse>> CreateAsync(CreateQuoteRequest request, IQuoteService service, CancellationToken ct)
    {
        var quote = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/quotes/{quote.Id}", quote);
    }

    internal static async Task<Ok<QuoteResponse>> GetAsync(Guid id, IQuoteService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<IReadOnlyList<QuoteResponse>>> ListAsync(
        IQuoteService service, CancellationToken ct, Guid? customerId = null, QuoteStatus? status = null) =>
        TypedResults.Ok(await service.ListAsync(customerId, status, ct));

    internal static async Task<Ok<QuoteResponse>> SendAsync(Guid id, IQuoteService service, CancellationToken ct, SendQuoteRequest? request = null) =>
        TypedResults.Ok(await service.SendAsync(id, request, ct));

    internal static async Task<Ok<QuoteResponse>> AcceptAsync(Guid id, IQuoteService service, CancellationToken ct, QuoteNoteRequest? request = null) =>
        TypedResults.Ok(await service.AcceptAsync(id, request, ct));

    internal static async Task<Ok<QuoteResponse>> RejectAsync(Guid id, IQuoteService service, CancellationToken ct, QuoteNoteRequest? request = null) =>
        TypedResults.Ok(await service.RejectAsync(id, request, ct));

    internal static async Task<Ok<IReadOnlyList<QuoteStateChangeResponse>>> HistoryAsync(Guid id, IQuoteService service, CancellationToken ct) =>
        TypedResults.Ok(await service.HistoryAsync(id, ct));
}
