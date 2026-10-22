using System.Net;
using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeDiningApiTests
{
    [Fact]
    public async Task Dining_api_uses_expected_routes_and_payloads_for_zones_and_tables()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        ZoneResponse zone = new()
        {
            Id = zoneId,
            Name = "Terraza",
            SortOrder = 2,
            RequestGuestCount = true,
        };
        TableResponse table = new()
        {
            Id = tableId,
            RestaurantId = restaurantId,
            ZoneId = zoneId,
            Code = "T01",
            Label = "Mesa 1",
            IsActive = true,
        };
        TableQrResponse qr = new(
            tableId,
            restaurantId,
            "T01",
            "Mesa 1",
            "qr-token",
            "/qr/qr-token",
            "/customer/qr-token"
        );
        RecordingHandler handler = new(
            Json(new List<ZoneResponse>()),
            Json(zone),
            Json(zone),
            Json(new List<TableResponse>()),
            Json(table),
            Json(table),
            new HttpResponseMessage(HttpStatusCode.NoContent),
            Json(qr)
        );
        BackofficeApi api = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
        );

        Assert.Empty(await api.ZonesAsync(restaurantId));
        ZoneResponse createdZone = await api.SaveZoneAsync(
            restaurantId,
            new ZoneResponse
            {
                Name = "Terraza",
                SortOrder = 2,
                RequestGuestCount = true,
            }
        );
        Assert.Equal(zoneId, createdZone.Id);
        Assert.Equal("Terraza", createdZone.Name);
        Assert.True(createdZone.RequestGuestCount);
        ZoneResponse updatedZone = await api.SaveZoneAsync(restaurantId, zone);
        Assert.Equal(zoneId, updatedZone.Id);
        Assert.Equal("Terraza", updatedZone.Name);
        Assert.Empty(await api.TablesAsync(restaurantId));
        TableResponse createdTable = await api.CreateTableAsync(
            restaurantId,
            new TableDraft
            {
                ZoneId = zoneId,
                Code = "T01",
                Label = "Mesa 1",
            }
        );
        Assert.Equal(tableId, createdTable.Id);
        Assert.Equal(zoneId, createdTable.ZoneId);
        TableResponse updatedTable = await api.UpdateTableAsync(table);
        Assert.Equal(tableId, updatedTable.Id);
        Assert.Equal("Mesa 1", updatedTable.Label);
        await api.DeleteTableAsync(tableId);
        Assert.Equal(qr, await api.RotateQrAsync(tableId));

        Assert.Equal(
            [
                (HttpMethod.Get, $"/api/dining/restaurants/{restaurantId}/zones"),
                (HttpMethod.Post, $"/api/dining/restaurants/{restaurantId}/zones"),
                (HttpMethod.Put, $"/api/dining/restaurants/{restaurantId}/zones/{zoneId}"),
                (HttpMethod.Get, $"/api/dining/restaurants/{restaurantId}/tables"),
                (HttpMethod.Post, $"/api/dining/restaurants/{restaurantId}/tables"),
                (HttpMethod.Put, $"/api/dining/tables/{tableId}"),
                (HttpMethod.Delete, $"/api/dining/tables/{tableId}"),
                (HttpMethod.Post, $"/api/dining/tables/{tableId}/qr/rotate"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
        Assert.Contains("\"requestGuestCount\":true", handler.Requests[1].Body);
        Assert.Contains("\"requestGuestCount\":true", handler.Requests[2].Body);
        Assert.Contains("\"zoneId\":", handler.Requests[4].Body);
        Assert.Contains("\"isActive\":true", handler.Requests[5].Body);
    }

    [Fact]
    public async Task Delete_table_propagates_api_error_detail()
    {
        RecordingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new { detail = "La mesa está ocupada." }),
            }
        );
        BackofficeApi api = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
        );

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.DeleteTableAsync(Guid.NewGuid())
        );

        Assert.Contains("API 409: La mesa está ocupada.", error.Message);
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, body)
            );
            return remaining.Dequeue();
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body);
}
