using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.Notifications;

/// <summary>Notification bell: <c>GET /api/notifications</c>.</summary>
public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationService, NotificationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/notifications", ListAsync)
            .WithTags("Notifications")
            .WithName("ListNotifications")
            .WithSummary("List notifications for the current user")
            .WithDescription($"Changes of tickets and connection requests from the last {NotificationService.WindowDays} days, newest first, "
                + $"at most {NotificationService.MaxItems}. A customer gets only own records, a technician only assigned tickets. "
                + "The user's own ticket changes are left out and draft connection requests are not reported.")
            .RequireAuthorization(AuthorizationPolicies.NotificationsRead);
    }

    internal static async Task<Ok<NotificationListResponse>> ListAsync(INotificationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(ct));
}
