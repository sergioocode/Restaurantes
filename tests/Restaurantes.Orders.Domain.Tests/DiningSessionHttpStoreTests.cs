using System.Net;
using Restaurantes.Orders.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class DiningSessionHttpStoreTests
{
    [Fact]
    public async Task EnsureOpen_sends_customer_token_and_maps_guest_count_and_zone()
    {
        Guid sessionId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        RecordingHandler handler = new(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                $"/api/dining/sessions/{sessionId}/validate",
                request.RequestUri!.AbsolutePath
            );
            Assert.Contains($"restaurantId={restaurantId}", request.RequestUri.Query);
            Assert.Contains($"tableId={tableId}", request.RequestUri.Query);
            Assert.Contains("source=Customer%20QR", request.RequestUri.Query);
            Assert.Contains("serviceMode=Take%20Away", request.RequestUri.Query);
            Assert.Equal(
                "customer-access-token",
                request.Headers.GetValues("X-Customer-Session-Token").Single()
            );
            HttpResponseMessage response = new(HttpStatusCode.NoContent);
            response.Headers.Add("X-Dining-Guest-Count", "3");
            response.Headers.Add("X-Dining-Zone-Id", zoneId.ToString());
            return response;
        });
        DiningSessionHttpStore store = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://dining.test") }
        );

        var result = await store.EnsureOpenAsync(
            sessionId,
            restaurantId,
            tableId,
            "Customer QR",
            "Take Away",
            "customer-access-token",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.GuestCount);
        Assert.Equal(zoneId, result.ZoneId);
    }

    [Fact]
    public async Task EnsureOpen_allows_missing_token_and_ignores_invalid_optional_headers()
    {
        RecordingHandler handler = new(request =>
        {
            Assert.False(request.Headers.Contains("X-Customer-Session-Token"));
            HttpResponseMessage response = new(HttpStatusCode.NoContent);
            response.Headers.Add("X-Dining-Guest-Count", "not-a-number");
            response.Headers.Add("X-Dining-Zone-Id", "not-a-guid");
            return response;
        });
        DiningSessionHttpStore store = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://dining.test") }
        );

        var result = await store.EnsureOpenAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "DineIn",
            " ",
            TestContext.Current.CancellationToken
        );

        Assert.Null(result.GuestCount);
        Assert.Null(result.ZoneId);
    }

    [Fact]
    public async Task EnsureOpen_rejects_non_open_sessions_and_wraps_transport_failures()
    {
        DiningSessionHttpStore closedStore = new(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );

        InvalidOperationException closed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            closedStore.EnsureOpenAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Pos",
                "DineIn",
                "",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("session is not open", closed.Message);
        Assert.Null(closed.InnerException);

        DiningSessionHttpStore offlineStore = new(
            new HttpClient(new RecordingHandler(_ => throw new HttpRequestException("offline")))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );
        InvalidOperationException offline = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                offlineStore.EnsureOpenAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Pos",
                    "DineIn",
                    "",
                    TestContext.Current.CancellationToken
                )
        );
        Assert.Contains("Dining is unavailable", offline.Message);
        Assert.IsType<HttpRequestException>(offline.InnerException);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }
}
