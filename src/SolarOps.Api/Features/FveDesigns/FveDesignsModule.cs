using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.FveDesigns;

/// <summary>Technical PV design (feature #20, BPMN #62): site survey, roof/consumption inputs, configuration, approval.</summary>
public sealed class FveDesignsModule : IModule
{
    /// <summary>Roles that may read designs and start new ones.</summary>
    internal static readonly string[] ReadRoles = [Roles.Admin, Roles.Sales, Roles.Designer];

    /// <summary>Roles that do the technical design work (BPMN lane "Projektant").</summary>
    internal static readonly string[] DesignerRoles = [Roles.Admin, Roles.Designer];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/designs").WithTags("FveDesigns");

        group.MapPost("/", FveDesignEndpoints.Create)
            .WithName("CreateFveDesign")
            .WithSummary("Request a PV design for a customer's supply point (status Requested)")
            .WithValidation<CreateFveDesignRequest>()
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/", FveDesignEndpoints.List)
            .WithName("ListFveDesigns")
            .WithSummary("List designs, optionally filtered by status, customer or supply point")
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/{id:guid}", FveDesignEndpoints.Get)
            .WithName("GetFveDesign")
            .WithSummary("Get a design with its roof and consumption parameters")
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapPost("/{id:guid}/site-survey", FveDesignEndpoints.RecordSiteSurvey)
            .WithName("RecordFveDesignSiteSurvey")
            .WithSummary("Record the site survey result (Requested → SiteSurveyDone)")
            .WithValidation<SiteSurveyRequest>()
            .RequireAuthorization(p => p.RequireRole(DesignerRoles));

        group.MapPost("/{id:guid}/mark-infeasible", FveDesignEndpoints.MarkInfeasible)
            .WithName("MarkFveDesignInfeasible")
            .WithSummary("Mark the site as not feasible (SiteSurveyDone → Infeasible)")
            .WithValidation<MarkInfeasibleRequest>()
            .RequireAuthorization(p => p.RequireRole(DesignerRoles));

        group.MapPut("/{id:guid}/parameters", FveDesignEndpoints.UpdateParameters)
            .WithName("UpdateFveDesignParameters")
            .WithSummary("Set roof planes, consumption and location (SiteSurveyDone/InDesign → InDesign)")
            .WithValidation<UpdateDesignParametersRequest>()
            .RequireAuthorization(p => p.RequireRole(DesignerRoles));
    }
}
