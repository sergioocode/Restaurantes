using System.Net;
using Restaurantes.Payments.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class DiningSessionValidatorTests
{
    [Fact]
    public async Task IsChargeAmountValid_rejects_invalid_authorization_without_request()
    {
        RecordingHandler handler = new(_ => throw new InvalidOperationException());
        DiningSessionValidator validator = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://dining.test") }
        );

        bool result = await validator.IsChargeAmountValidAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5m,
            ":",
            TestContext.Current.CancellationToken
        );

        Assert.False(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task IsChargeAmountValid_matches_order_restaurant_and_exact_amount()
    {
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        RecordingHandler handler = new(request =>
        {
            Assert.Equal(
                $"/api/dining/sessions/{sessionId}/bill",
                request.RequestUri!.AbsolutePath
            );
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return JsonResponse(
                $$"""{"restaurantId":"{{restaurantId}}","orders":[{"orderId":"{{orderId}}","amount":12.5}]}"""
            );
        });
        DiningSessionValidator validator = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://dining.test") }
        );

        bool matches = await validator.IsChargeAmountValidAsync(
            sessionId,
            orderId,
            restaurantId,
            12.5m,
            "Bearer access-token",
            TestContext.Current.CancellationToken
        );
        bool wrongAmount = await validator.IsChargeAmountValidAsync(
            sessionId,
            orderId,
            restaurantId,
            12.51m,
            "Bearer access-token",
            TestContext.Current.CancellationToken
        );

        Assert.True(matches);
        Assert.False(wrongAmount);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task IsChargeAmountValid_rejects_unavailable_or_invalid_bill_responses()
    {
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Queue<HttpResponseMessage> responses = new([
            new HttpResponseMessage(HttpStatusCode.NotFound),
            JsonResponse("{\"restaurantId\":\"broken\",\"orders\":[]}"),
            JsonResponse("{\"restaurantId\":\"" + Guid.NewGuid() + "\",\"orders\":[]}"),
            JsonResponse("{\"restaurantId\":\"" + restaurantId + "\"}"),
        ]);
        DiningSessionValidator validator = new(
            new HttpClient(new RecordingHandler(_ => responses.Dequeue()))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );

        for (int index = 0; index < 4; index++)
        {
            Assert.False(
                await validator.IsChargeAmountValidAsync(
                    Guid.NewGuid(),
                    orderId,
                    restaurantId,
                    12m,
                    "Bearer access-token",
                    TestContext.Current.CancellationToken
                )
            );
        }

        DiningSessionValidator offlineValidator = new(
            new HttpClient(new RecordingHandler(_ => throw new HttpRequestException()))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );
        Assert.False(
            await offlineValidator.IsChargeAmountValidAsync(
                Guid.NewGuid(),
                orderId,
                restaurantId,
                12m,
                "Bearer access-token",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task IsValid_requires_no_content_and_handles_connection_failure()
    {
        Guid sessionId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        RecordingHandler successHandler = new(request =>
        {
            Assert.Contains($"restaurantId={restaurantId}", request.RequestUri!.Query);
            Assert.Contains($"tableId={tableId}", request.RequestUri.Query);
            Assert.Contains("source=CustomerQr", request.RequestUri.Query);
            Assert.Contains("serviceMode=TAKE%20AWAY", request.RequestUri.Query);
            Assert.Equal(
                "customer-token",
                request.Headers.GetValues("X-Customer-Session-Token").Single()
            );
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        DiningSessionValidator validator = new(
            new HttpClient(successHandler) { BaseAddress = new Uri("http://dining.test") }
        );

        Assert.True(
            await validator.IsValidAsync(
                sessionId,
                restaurantId,
                tableId,
                "TAKE AWAY",
                "customer-token",
                TestContext.Current.CancellationToken
            )
        );

        DiningSessionValidator badResponseValidator = new(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );
        Assert.False(
            await badResponseValidator.IsValidAsync(
                sessionId,
                restaurantId,
                tableId,
                "DineIn",
                "customer-token",
                TestContext.Current.CancellationToken
            )
        );

        DiningSessionValidator offlineValidator = new(
            new HttpClient(new RecordingHandler(_ => throw new HttpRequestException()))
            {
                BaseAddress = new Uri("http://dining.test"),
            }
        );
        Assert.False(
            await offlineValidator.IsValidAsync(
                sessionId,
                restaurantId,
                tableId,
                "DineIn",
                "customer-token",
                TestContext.Current.CancellationToken
            )
        );
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }
}
