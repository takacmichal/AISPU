using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Contracts;

/// <summary>Builds customers and accepted quotes through the API for contract tests.</summary>
internal static class ContractTestData
{
    internal static async Task<CustomerResponse> NewCustomerAsync(HttpClient client) =>
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person($"{Guid.NewGuid():N}@example.sk"));

    /// <summary>Creates a quote of two panels (100 each) and one hour of labor (35) and moves it to <paramref name="target"/>.</summary>
    internal static async Task<QuoteResponse> NewQuoteAsync(HttpClient client, CustomerResponse customer, QuoteStatus target = QuoteStatus.Accepted)
    {
        using var panelResponse = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = $"PAN-{Guid.NewGuid():N}",
            Name = "Panel 450 W",
            PowerW = 450,
            UnitPrice = 100m,
        });
        var panel = await panelResponse.ReadJsonAsync<ComponentResponse>();
        using var laborResponse = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = ComponentType.Labor,
            Code = $"LAB-{Guid.NewGuid():N}",
            Name = "Montáž",
            UnitPrice = 35m,
        });
        var labor = await laborResponse.ReadJsonAsync<ComponentResponse>();

        using var created = await client.PostJsonAsync("/api/quotes", new
        {
            customerId = customer.Id,
            lines = new[] { new { componentId = panel.Id, quantity = 2m }, new { componentId = labor.Id, quantity = 1m } },
        });
        var quote = await created.ReadJsonAsync<QuoteResponse>();

        if (target != QuoteStatus.Draft)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/send", null)).EnsureSuccessStatusCode();
        }

        if (target == QuoteStatus.Accepted)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/accept", null)).EnsureSuccessStatusCode();
        }
        else if (target == QuoteStatus.Rejected)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/reject", null)).EnsureSuccessStatusCode();
        }

        return await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{quote.Id}");
    }

    internal static Task<HttpResponseMessage> CreateContractAsync(HttpClient client, Guid quoteId) =>
        client.PostJsonAsync("/api/contracts", new { quoteId });
}
