using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.Notifications;

/// <summary>
/// Pure mapping from entity state to a notification: id, title and link. The states of the ticket audiences are
/// disjoint (New/Triaged staff, VisitScheduled/OnSite technician, Resolved customer), so the title depends on the state alone.
/// </summary>
public static class NotificationMapping
{
    public const string TicketKind = "ticket";
    public const string ConnectionRequestKind = "connection_request";

    public static NotificationItemResponse Ticket(Guid ticketId, ServiceTicketState state, string ticketTitle, DateTimeOffset createdAt) =>
        new(
            $"{TicketKind}:{ticketId:D}:{state}",
            NotificationKind.Ticket,
            TicketTitle(state, ticketTitle),
            createdAt,
            $"/tickets/{ticketId:D}");

    /// <param name="requestId">Id of the connection request.</param>
    /// <param name="state">Current state of the request.</param>
    /// <param name="customerName">Display name of the customer.</param>
    /// <param name="createdAt">Time the request entered its state.</param>
    /// <param name="forCustomer">True when the item is shown to the customer; the staff wording is used otherwise.</param>
    public static NotificationItemResponse ConnectionRequest(
        Guid requestId, ConnectionRequestState state, string customerName, DateTimeOffset createdAt, bool forCustomer) =>
        new(
            $"{ConnectionRequestKind}:{requestId:D}:{state}",
            NotificationKind.ConnectionRequest,
            ConnectionRequestTitle(state, customerName, forCustomer),
            createdAt,
            $"/connection-requests/{requestId:D}");

    public static string TicketTitle(ServiceTicketState state, string ticketTitle) => state switch
    {
        ServiceTicketState.New => $"Nový servisný tiket: {ticketTitle}",
        ServiceTicketState.Triaged => $"Tiket čaká na plánovanie výjazdu: {ticketTitle}",
        ServiceTicketState.VisitScheduled or ServiceTicketState.OnSite => $"Výjazd k tiketu: {ticketTitle}",
        ServiceTicketState.Resolved => $"Tiket čaká na vaše potvrdenie: {ticketTitle}",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No notification for this ticket state."),
    };

    public static string ConnectionRequestTitle(ConnectionRequestState state, string customerName, bool forCustomer) => state switch
    {
        ConnectionRequestState.Submitted => $"Nová žiadosť o pripojenie: {customerName}",
        ConnectionRequestState.InfoRequested when forCustomer =>
            $"Žiadosť o pripojenie vyžaduje doplnenie údajov: {customerName}",
        ConnectionRequestState.InfoRequested => $"Žiadosť vyžaduje doplnenie údajov: {customerName}",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No notification for this connection request state."),
    };
}
