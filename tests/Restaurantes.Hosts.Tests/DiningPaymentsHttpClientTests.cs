using System.Net;
using System.Text.Json;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningPaymentsHttpClientTests
{
    [Fact]
    public async Task Adjust_amount_sends_payment_route_body_and_valid_bearer_header()
    {
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        DiningPayments payments = CreatePayments(handler);

        DiningPaymentResult result = await payments.AdjustAmountAsync(
            orderId,
            sessionId,
            12.5m,
            "Bearer access-token",
            CancellationToken.None
        );

        Assert.True(result.Succeeded);
        Assert.Equal(204, result.StatusCode);
        Assert.Equal("", result.Body);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal($"https://payments.test/api/payments/orders/{orderId}/amount", handler.Uri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("access-token", handler.AuthorizationParameter);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(12.5m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(sessionId, body.RootElement.GetProperty("diningSessionId").GetGuid());
    }

    [Fact]
    public async Task Adjust_amount_without_authorization_header_returns_failure_body()
    {
        Guid orderId = Guid.NewGuid();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("payment order not found"),
        });
        DiningPayments payments = CreatePayments(handler);

        DiningPaymentResult result = await payments.AdjustAmountAsync(
            orderId,
            Guid.NewGuid(),
            8m,
            null,
            CancellationToken.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("payment order not found", result.Body);
        Assert.Null(handler.AuthorizationScheme);
    }

    [Fact]
    public async Task Capture_reads_paid_amounts_from_a_successful_payment_response()
    {
        Guid orderId = Guid.NewGuid();
        DiningPaymentRequest request = CreatePaymentRequest();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"amountPaid\":14.5,\"tipAmountPaid\":1.25}"),
        });
        DiningPayments payments = CreatePayments(handler);

        DiningPaymentResult result = await payments.CaptureAsync(
            orderId,
            request,
            null,
            CancellationToken.None
        );

        Assert.True(result.Succeeded);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(14.5m, result.AmountPaid);
        Assert.Equal(1.25m, result.TipAmountPaid);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"https://payments.test/api/payments/orders/{orderId}/capture", handler.Uri);
        Assert.Null(handler.AuthorizationScheme);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            request.IdempotencyKey,
            body.RootElement.GetProperty("idempotencyKey").GetGuid()
        );
    }

    [Fact]
    public async Task Capture_returns_non_success_response_without_parsing_its_body()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage((HttpStatusCode)425)
        {
            Content = new StringContent("payment projection is not ready"),
        });
        DiningPayments payments = CreatePayments(handler);

        DiningPaymentResult result = await payments.CaptureAsync(
            Guid.NewGuid(),
            CreatePaymentRequest(),
            "Bearer payment-token",
            CancellationToken.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal(425, result.StatusCode);
        Assert.Equal("payment projection is not ready", result.Body);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("payment-token", handler.AuthorizationParameter);
        Assert.Equal(0m, result.AmountPaid);
        Assert.Equal(0m, result.TipAmountPaid);
    }

    [Fact]
    public async Task Capture_defaults_missing_paid_amounts_to_zero()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        });
        DiningPayments payments = CreatePayments(handler);

        DiningPaymentResult result = await payments.CaptureAsync(
            Guid.NewGuid(),
            CreatePaymentRequest(),
            null,
            CancellationToken.None
        );

        Assert.True(result.Succeeded);
        Assert.Equal(0m, result.AmountPaid);
        Assert.Equal(0m, result.TipAmountPaid);
    }

    private static DiningPayments CreatePayments(RecordingHandler handler)
    {
        HttpClient client = new(handler) { BaseAddress = new Uri("https://payments.test") };
        return new DiningPayments(new SingleClientFactory(client));
    }

    private static DiningPaymentRequest CreatePaymentRequest() =>
        new(Guid.NewGuid(), 2, 14.5m, 1.25m, "Card", "POS");

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("payments", name);
            return client;
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory
    ) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Method = request.Method;
            Uri = request.RequestUri?.ToString();
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
