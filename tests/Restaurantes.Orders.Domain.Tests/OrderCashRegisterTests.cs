using System.Net;
using System.Net.Http.Json;
using System.Text;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderCashRegisterTests
{
    [Fact]
    public async Task Business_date_is_read_from_the_restaurants_cash_register()
    {
        Guid restaurantId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 22);
        RecordingHandler handler = new(_ => JsonResponse(true, Guid.NewGuid(), businessDate));
        OrderCashRegister cashRegister = CreateCashRegister(handler);

        DateOnly result = await cashRegister.GetBusinessDateAsync(
            restaurantId,
            CancellationToken.None
        );

        Assert.Equal(businessDate, result);
        Assert.Equal(
            $"https://cash-register.test/api/cash-register/restaurants/{restaurantId}/is-open",
            handler.RequestUri!.ToString()
        );
    }

    [Fact]
    public async Task Ensure_open_returns_session_id_and_business_date()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 22);
        OrderCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(_ => JsonResponse(true, sessionId, businessDate))
        );

        OrderCashRegisterSession result = await cashRegister.EnsureOpenAsync(
            restaurantId,
            CancellationToken.None
        );

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(businessDate, result.BusinessDate);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Ensure_open_rejects_closed_or_incomplete_register(
        bool isOpen,
        bool hasSession
    )
    {
        Guid restaurantId = Guid.NewGuid();
        OrderCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(_ =>
                JsonResponse(isOpen, hasSession ? Guid.NewGuid() : null, new DateOnly(2026, 10, 22))
            )
        );

        OrderCashRegisterClosedException error =
            await Assert.ThrowsAsync<OrderCashRegisterClosedException>(() =>
                cashRegister.EnsureOpenAsync(restaurantId, CancellationToken.None)
            );

        Assert.Contains(restaurantId.ToString(), error.Message);
    }

    [Fact]
    public async Task Availability_requires_a_successful_response_with_a_body()
    {
        Guid restaurantId = Guid.NewGuid();
        OrderCashRegister unavailable = CreateCashRegister(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        );
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            unavailable.GetBusinessDateAsync(restaurantId, CancellationToken.None)
        );

        OrderCashRegister empty = CreateCashRegister(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json"),
            })
        );
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            empty.GetBusinessDateAsync(restaurantId, CancellationToken.None)
        );
        Assert.Equal("CashRegister returned an empty operational status.", error.Message);
    }

    private static OrderCashRegister CreateCashRegister(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://cash-register.test/") });

    private static HttpResponseMessage JsonResponse(
        bool isOpen,
        Guid? sessionId,
        DateOnly businessDate
    ) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new Availability(isOpen, sessionId, businessDate)),
        };

    private sealed record Availability(bool IsOpen, Guid? SessionId, DateOnly BusinessDate);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(response(request));
        }
    }
}
