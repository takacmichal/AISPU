using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.Notifications;

/// <summary>Notification bell: <c>GET /api/notifications</c>, computed on read for the current user.</summary>
public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationService, NotificationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("/", ListAsync)
            .WithName("ListNotifications")
            .WithSummary("List notifications for the current user")
            .WithDescription("Newest first.")
            .RequireAuthorization(AuthorizationPolicies.NotificationsRead);
    }

    internal static async Task<Ok<NotificationListResponse>> ListAsync(INotificationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(ct));
}
