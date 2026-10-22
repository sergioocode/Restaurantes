using System.Net;
using System.Text;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningCashRegisterHttpClientTests
{
    [Fact]
    public async Task EnsureOpen_sends_restaurant_id_and_accepts_open_register()
    {
        Guid restaurantId = Guid.NewGuid();
        RecordingHandler handler = new(_ => JsonResponse("{\"isOpen\":true}"));
        DiningCashRegister cashRegister = CreateClient(handler);

        await cashRegister.EnsureOpenAsync(restaurantId, CancellationToken.None);

        Assert.Equal($"api/cash-register/restaurants/{restaurantId}/is-open", handler.RequestUri);
    }

    [Fact]
    public async Task EnsureOpen_rejects_closed_register_and_empty_status()
    {
        DiningCashRegister closed = CreateClient(
            new RecordingHandler(_ => JsonResponse("{\"isOpen\":false}"))
        );
        DiningCashRegister empty = CreateClient(new RecordingHandler(_ => JsonResponse("null")));

        await Assert.ThrowsAsync<DiningCashRegisterClosedException>(() =>
            closed.EnsureOpenAsync(Guid.NewGuid(), CancellationToken.None)
        );
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            empty.EnsureOpenAsync(Guid.NewGuid(), CancellationToken.None)
        );
        Assert.Contains("empty operational status", error.Message);
    }

    [Fact]
    public async Task EnsureOpen_propagates_http_errors_and_cancellation()
    {
        DiningCashRegister unavailable = CreateClient(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        );
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        DiningCashRegister cancelled = CreateClient(
            new RecordingHandler(_ => throw new OperationCanceledException())
        );

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            unavailable.EnsureOpenAsync(Guid.NewGuid(), CancellationToken.None)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.EnsureOpenAsync(Guid.NewGuid(), cancellation.Token)
        );
    }

    private static DiningCashRegister CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://cash-register/") });

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri?.PathAndQuery.TrimStart('/');
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
}
