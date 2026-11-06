using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Search;

/// <summary>Global search box: <c>GET /api/search</c>.</summary>
public sealed class SearchModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISearchService, SearchService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/search").WithTags("Search");

        group.MapGet("/", SearchAsync)
            .WithName("Search")
            .WithSummary("Search customers, quotes, installations and tickets")
            .WithDescription("q is 2 to 200 characters (case-insensitive substring), limit is the maximum hits per group (default 5, max 10). "
                + "Customers match name, e-mail, phone and IČO; quotes match the customer name and note; installations match their name, supply point name or address and customer name; "
                + "tickets match the title and customer name. A group is null when the caller's role may not read that module; a customer account never gets customers or quotes and only its own installations and tickets.")
            .WithValidation<SearchQuery>()
            .RequireAuthorization(AuthorizationPolicies.SearchRead);
    }

    internal static async Task<Ok<SearchResponse>> SearchAsync([AsParameters] SearchQuery query, ISearchService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SearchAsync(query, ct));
}
