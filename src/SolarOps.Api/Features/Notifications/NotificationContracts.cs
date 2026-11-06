using System.Text.Json.Serialization;

namespace SolarOps.Api.Features.Notifications;

/// <summary>One item of the notification list. The frontend builds nothing itself: <see cref="Link"/> is the route to open.</summary>
/// <param name="Id">Deterministic per (kind, entity, state), e.g. <c>ticket:{ticketId}:{state}</c>; the frontend remembers seen ids.</param>
/// <param name="Kind"><c>ticket</c> or <c>connection_request</c>.</param>
/// <param name="Title">Slovak text for the user.</param>
/// <param name="CreatedAt">Time the entity entered the state (UTC).</param>
/// <param name="Link">Frontend route, <c>/tickets/{id}</c> or <c>/connection-requests/{id}</c>.</param>
public sealed record NotificationItemResponse(string Id, NotificationKind Kind, string Title, DateTimeOffset CreatedAt, string Link);

/// <summary>Kind of a notification; serialized as <c>ticket</c> or <c>connection_request</c>.</summary>
public enum NotificationKind
{
    [JsonStringEnumMemberName("ticket")]
    Ticket,

    [JsonStringEnumMemberName("connection_request")]
    ConnectionRequest,
}

/// <summary>Response of <c>GET /api/notifications</c>: at most 50 items, newest first.</summary>
public sealed record NotificationListResponse(IReadOnlyList<NotificationItemResponse> Items);

public interface INotificationService
{
    Task<NotificationListResponse> ListAsync(CancellationToken ct);
}
