using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.Notifications;

/// <summary>Slovak state names shown in notifications, the same as in the frontend screens of these records.</summary>
internal static class NotificationTexts
{
    public static string Label(ServiceTicketState state) => state switch
    {
        ServiceTicketState.New => "Nový",
        ServiceTicketState.Triaged => "Zatriedený",
        ServiceTicketState.RemoteDiagnosis => "Diagnostika na diaľku",
        ServiceTicketState.VisitScheduled => "Výjazd naplánovaný",
        ServiceTicketState.OnSite => "Technik na mieste",
        ServiceTicketState.Verified => "Overený",
        ServiceTicketState.Resolved => "Vyriešený",
        ServiceTicketState.Closed => "Uzavretý",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Label(ConnectionRequestState state) => state switch
    {
        ConnectionRequestState.Submitted => "Podaná distribučke",
        ConnectionRequestState.InfoRequested => "Distribučka žiada doplnenie",
        ConnectionRequestState.Rejected => "Zamietnutá",
        ConnectionRequestState.ContractOffered => "Zmluva ponúknutá",
        ConnectionRequestState.ContractSigned => "Zmluva podpísaná zákazníkom",
        ConnectionRequestState.ContractSubmitted => "Podpísaná zmluva odoslaná",
        ConnectionRequestState.Concluded => "Uzavretá",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
