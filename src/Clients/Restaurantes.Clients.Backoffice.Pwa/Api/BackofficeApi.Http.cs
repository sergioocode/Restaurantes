using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    private async Task<T> GetAsync<T>(string path)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, path);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<T>(response);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("La API devolvió una respuesta vacía.");
        }
        string content = await response.Content.ReadAsStringAsync();
        string detail = content;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (
                json.RootElement.TryGetProperty("detail", out JsonElement value)
                || json.RootElement.TryGetProperty("title", out value)
            )
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ReadAsync<object>(response);
    }
}
