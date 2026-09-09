using System.Net.Http.Json;
using System.Text.Json;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
    static HttpRequestMessage WithCustomerToken(HttpMethod method, string path, string? token)
    {
        HttpRequestMessage request = new(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("X-Customer-Session-Token", token);
        }
        return request;
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new CustomerApiException(
                    response.StatusCode,
                    "The API returned an empty response."
                );
        }

        string content = await response.Content.ReadAsStringAsync();
        string detail = content;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("detail", out JsonElement value))
            {
                detail = value.GetString() ?? content;
            }
            else if (json.RootElement.TryGetProperty("title", out value))
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }

        throw new CustomerApiException(response.StatusCode, detail);
    }
}
