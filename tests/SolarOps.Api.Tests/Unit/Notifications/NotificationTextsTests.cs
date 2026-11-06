using System.Text.Json;
using System.Text.Json.Serialization;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Notifications;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Tests.Unit.Notifications;

public sealed class NotificationTextsTests
{
    [Fact]
    public void EveryTicketState_HasALabel()
    {
        foreach (var state in Enum.GetValues<ServiceTicketState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(NotificationTexts.Label(state)));
        }
    }

    [Fact]
    public void EveryConnectionRequestState_ExceptDraft_HasALabel()
    {
        foreach (var state in Enum.GetValues<ConnectionRequestState>().Where(s => s != ConnectionRequestState.Draft))
        {
            Assert.False(string.IsNullOrWhiteSpace(NotificationTexts.Label(state)));
        }
    }

    /// <summary>The notification bell in the frontend looks up these exact values.</summary>
    [Theory]
    [InlineData(NotificationKind.Ticket, "\"ticket\"")]
    [InlineData(NotificationKind.ConnectionRequest, "\"connection_request\"")]
    public void Kind_IsSerializedAsTheContractValue(NotificationKind kind, string expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        Assert.Equal(expected, JsonSerializer.Serialize(kind, options));
    }
}
