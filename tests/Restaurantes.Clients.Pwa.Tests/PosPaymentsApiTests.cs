using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosPaymentsApiTests
{
    [Fact]
    public async Task Payment_status_queries_send_the_order_ids_and_parse_statuses()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        RecordingHandler handler = new(
            Json(new[] { new OrderPaymentStatusResponse(orderId, "Paid") })
        );
        PosApi api = new(new HttpClient(handler) { BaseAddress = new Uri("https://test.local") });

        OrderPaymentStatusResponse single = Assert.Single(
            await api.PaymentStatusesAsync(restaurantId, orderId)
        );

        Assert.Equal(orderId, single.OrderId);
        Assert.Equal("Paid", single.Status);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            $"/api/payments/restaurants/{restaurantId}/order-statuses",
            handler.Requests[0].Path
        );
        Assert.Contains(orderId.ToString(), handler.Requests[0].Body);
    }

    [Fact]
    public async Task Pickup_capture_retries_projection_not_found_and_builds_cash_register_reference()
    {
        Guid orderId = Guid.NewGuid();
        Guid cashRegisterId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        RecordingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage((HttpStatusCode)425),
            new HttpResponseMessage(HttpStatusCode.NoContent)
        );
        PosApi api = new(new HttpClient(handler) { BaseAddress = new Uri("https://test.local") });

        await api.CapturePickupAsync(orderId, "Cash", cashRegisterId, "  ", idempotencyKey);

        Assert.Equal(3, handler.Requests.Count);
        RecordedRequest request = handler.Requests[^1];
        Assert.Equal($"/api/payments/orders/{orderId}/capture", request.Path);
        Assert.Contains(idempotencyKey.ToString(), request.Body);
        Assert.Contains("CASHREGISTER:" + cashRegisterId, request.Body);
        Assert.Contains($"POS-PICKUP-{orderId:N}", request.Body);
    }

    [Fact]
    public async Task Pickup_capture_surfaces_non_retryable_payment_errors_immediately()
    {
        Guid orderId = Guid.NewGuid();
        RecordingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { detail = "Payments is unavailable." }),
            },
            new HttpResponseMessage(HttpStatusCode.NoContent)
        );
        PosApi api = new(new HttpClient(handler) { BaseAddress = new Uri("https://test.local") });

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.CapturePickupAsync(orderId, "Card", null, "ref-1", Guid.NewGuid())
        );

        Assert.Equal("API 503: Payments is unavailable.", error.Message);
        Assert.Single(handler.Requests);
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
