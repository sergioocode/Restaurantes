using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    private static async Task RetryAsync(
        Func<Task<HttpResponseMessage>> send,
        int[] retryStatusCodes,
        string timeoutMessage
    )
    {
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            using HttpResponseMessage response = await send();
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            if (!retryStatusCodes.Contains((int)response.StatusCode))
            {
                await ReadAsync<object>(response);
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException(timeoutMessage);
    }

    public static string PaymentReference(
        string method,
        Guid? cashRegisterSessionId,
        string reference
    )
    {
        return cashRegisterSessionId.HasValue
            ? $"CASHREGISTER:{cashRegisterSessionId.Value};{reference}"
            : reference;
    }

    private static async Task<T> RetryAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        int[] retryStatusCodes,
        string timeoutMessage,
        Func<HttpResponseMessage, Task<T>> read
    )
    {
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            using HttpResponseMessage response = await send();
            if (response.IsSuccessStatusCode)
            {
                return await read(response);
            }
            if (!retryStatusCodes.Contains((int)response.StatusCode))
            {
                return await read(response);
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException(timeoutMessage);
    }

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
            if (json.RootElement.TryGetProperty("detail", out JsonElement value))
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }
}
