using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>JSON options matching the API (web defaults + enums as strings) and helpers using them.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Options);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Options);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Options)
        ?? throw new InvalidOperationException("Response body was empty.");

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<T>();
    }
}
