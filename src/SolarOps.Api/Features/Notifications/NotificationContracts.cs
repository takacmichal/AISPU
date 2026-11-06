using System.Text.Json.Serialization;

namespace SolarOps.Api.Features.Notifications;

/// <summary>What a notification is about; the wire values are the ones the notification bell expects.</summary>
public enum NotificationKind
{
    [JsonStringEnumMemberName("ticket")]
    Ticket,

    [JsonStringEnumMemberName("connection_request")]
    ConnectionRequest,
}

/// <summary>One entry of the notification bell.</summary>
/// <param name="Id">Stable for one event, so the client can remember which notifications were seen.</param>
/// <param name="Kind">Type of the record the notification is about.</param>
/// <param name="Title">Slovak text for the user.</param>
/// <param name="CreatedAt">When the change happened.</param>
/// <param name="Link">Frontend route of the record.</param>
public sealed record NotificationItemResponse(string Id, NotificationKind Kind, string Title, DateTimeOffset CreatedAt, string Link);

/// <summary>Notifications of the current user, newest first.</summary>
public sealed record NotificationListResponse(IReadOnlyList<NotificationItemResponse> Items);
