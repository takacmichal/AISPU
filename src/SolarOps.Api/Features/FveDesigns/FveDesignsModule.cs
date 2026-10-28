using SolarOps.Api.Features.Quotes;
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

    /// <summary>
    /// Roles that decide whether the design suits (BPMN lane "Obchod", ADR #207: Sales approves and sends back)
    /// and that turn an approved design into a quote.
    /// TODO(after #335): replace with the named authorization policy.
    /// </summary>
    internal static readonly string[] ApproverRoles = [Roles.Admin, Roles.Sales];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Swap for a PVGIS / prediction-service client by registering another IYieldEstimator.
        services.AddSingleton<IYieldEstimator, SimpleYieldEstimator>();
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

        group.MapPost("/{id:guid}/configuration/preview", FveDesignVersionEndpoints.Preview)
            .WithName("PreviewFveDesignConfiguration")
            .WithSummary("Check panel/inverter compatibility and estimate annual yield without saving")
            .WithValidation<ConfigureDesignRequest>()
            .RequireAuthorization(p => p.RequireRole(DesignerRoles));

        group.MapPost("/{id:guid}/versions", FveDesignVersionEndpoints.Save)
            .WithName("SaveFveDesignVersion")
            .WithSummary("Save the configuration as a new immutable version (400 when incompatible, 409 when the design is not InDesign)")
            .WithValidation<ConfigureDesignRequest>()
            .RequireAuthorization(p => p.RequireRole(DesignerRoles));

        group.MapPost("/{id:guid}/approve", FveDesignEndpoints.Approve)
            .WithName("ApproveFveDesign")
            .WithSummary("Approve the design (InDesign → Approved); the newest saved version becomes the approved one")
            .WithDescription("409 when the design is not InDesign or has no saved configuration version. The approver is recorded in the response (approval).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(ApproverRoles));

        group.MapPost("/{id:guid}/rework", FveDesignEndpoints.Rework)
            .WithName("ReworkFveDesign")
            .WithSummary("Send the design back to the designer (InDesign/Approved → InDesign)")
            .WithDescription("Withdraws an existing approval. Draft and Sent quotes built from the design are marked as needing recalculation.")
            .WithValidation<ReworkDesignRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(ApproverRoles));

        group.MapPost("/{id:guid}/quote", FveDesignEndpoints.CreateQuote)
            .WithName("CreateQuoteFromFveDesign")
            .WithSummary("Create a Draft quote from the approved design")
            .WithDescription("Lines are the panels and the inverter of the approved version, priced as saved in that version. "
                + "409 when the design is not Approved or its customer is archived; 400 when the customer does not exist.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(QuotesModule.WriteRoles));

        group.MapGet("/{id:guid}/versions", FveDesignVersionEndpoints.List)
            .WithName("ListFveDesignVersions")
            .WithSummary("List saved configuration versions, newest first")
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/{id:guid}/versions/{number:int}", FveDesignVersionEndpoints.Get)
            .WithName("GetFveDesignVersion")
            .WithSummary("Get one saved configuration version")
            .RequireAuthorization(p => p.RequireRole(ReadRoles));
    }
}
