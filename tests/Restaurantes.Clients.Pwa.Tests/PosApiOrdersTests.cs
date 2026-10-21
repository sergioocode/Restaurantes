using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosApiOrdersTests
{
    [Fact]
    public async Task Dine_in_order_uses_the_selected_table_and_submits_the_created_order()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderResponse response = new(
            orderId,
            restaurantId,
            tableId,
            sessionId,
            "Cliente",
            "DineIn",
            "OnAccount",
            "Submitted",
            8m,
            DateTime.UtcNow
        );
        RecordingHandler handler = new(response);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        PosApi api = new(http) { AccessToken = "pos-token" };

        OrderResponse result = await api.CreateAndSubmitAsync(
            restaurantId,
            new TableResponse(
                tableId,
                restaurantId,
                "T04",
                "Mesa 04",
                true,
                "Occupied",
                sessionId,
                Guid.NewGuid(),
                "Salón",
                1
            ),
            sessionId,
            "DineIn",
            "Cliente",
            [
                new PosCartLine
                {
                    ProductId = productId,
                    Quantity = 1,
                    ExpectedUnitPrice = 8m,
                },
            ]
        );

        Assert.Equal(orderId, result.Id);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/orders", request.Path);
                Assert.Equal("Bearer pos-token", request.Authorization);
                using JsonDocument body = JsonDocument.Parse(request.Body!);
                Assert.Equal(restaurantId, body.RootElement.GetProperty("restaurantId").GetGuid());
                Assert.Equal(tableId, body.RootElement.GetProperty("tableId").GetGuid());
                Assert.Equal(sessionId, body.RootElement.GetProperty("diningSessionId").GetGuid());
                Assert.Equal("Mesa 04", body.RootElement.GetProperty("tableLabel").GetString());
                Assert.Equal("DineIn", body.RootElement.GetProperty("serviceMode").GetString());
                Assert.Equal(
                    productId,
                    body.RootElement.GetProperty("lines")[0].GetProperty("productId").GetGuid()
                );
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal($"/api/orders/{orderId}/submit", request.Path);
                Assert.Null(request.Body);
                Assert.Equal("Bearer pos-token", request.Authorization);
            }
        );
    }

    private sealed class RecordingHandler(OrderResponse response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri!.PathAndQuery,
                    request.Headers.Authorization?.ToString(),
                    body
                )
            );
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response),
            };
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string? Body
    );
}
