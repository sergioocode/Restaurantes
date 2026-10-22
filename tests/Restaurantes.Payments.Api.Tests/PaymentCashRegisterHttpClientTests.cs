using System.Net;
using System.Net.Http.Json;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentCashRegisterHttpClientTests
{
    [Fact]
    public async Task Reads_open_register_session_and_business_date()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 22);
        RecordingHandler handler = new(
            HttpStatusCode.OK,
            JsonContent.Create(
                new
                {
                    isOpen = true,
                    sessionId,
                    businessDate,
                }
            )
        );
        PaymentCashRegister cashRegister = CreateCashRegister(handler);

        CashRegisterSessionDetails session = await cashRegister.GetOpenSessionAsync(
            restaurantId,
            CancellationToken.None
        );

        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(businessDate, session.BusinessDate);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            $"https://cash-register.test/api/cash-register/restaurants/{restaurantId}/is-open",
            handler.Uri
        );
    }

    [Fact]
    public async Task Rejects_a_closed_register()
    {
        Guid restaurantId = Guid.NewGuid();
        PaymentCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(
                HttpStatusCode.OK,
                JsonContent.Create(
                    new
                    {
                        isOpen = false,
                        sessionId = (Guid?)null,
                        businessDate = new DateOnly(2026, 10, 22),
                    }
                )
            )
        );

        PaymentCashRegisterClosedException exception =
            await Assert.ThrowsAsync<PaymentCashRegisterClosedException>(() =>
                cashRegister.GetOpenSessionAsync(restaurantId, CancellationToken.None)
            );

        Assert.Contains(restaurantId.ToString(), exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Rejects_an_open_register_without_a_valid_session(string? sessionText)
    {
        Guid? sessionId = sessionText is null ? null : Guid.Parse(sessionText);
        PaymentCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(
                HttpStatusCode.OK,
                JsonContent.Create(
                    new
                    {
                        isOpen = true,
                        sessionId,
                        businessDate = new DateOnly(2026, 10, 22),
                    }
                )
            )
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                cashRegister.GetOpenSessionAsync(Guid.NewGuid(), CancellationToken.None)
        );

        Assert.Equal("CashRegister did not return an open session id.", exception.Message);
    }

    [Fact]
    public async Task Rejects_an_empty_operational_response()
    {
        PaymentCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(HttpStatusCode.OK, JsonContent.Create<object?>(null))
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                cashRegister.GetOpenSessionAsync(Guid.NewGuid(), CancellationToken.None)
        );

        Assert.Equal("CashRegister returned an empty operational status.", exception.Message);
    }

    [Fact]
    public async Task Propagates_cash_register_http_errors()
    {
        PaymentCashRegister cashRegister = CreateCashRegister(
            new RecordingHandler(HttpStatusCode.ServiceUnavailable, new StringContent("offline"))
        );

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cashRegister.GetOpenSessionAsync(Guid.NewGuid(), CancellationToken.None)
        );
    }

    private static PaymentCashRegister CreateCashRegister(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://cash-register.test"),
        };
        return new PaymentCashRegister(httpClient);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, HttpContent content)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Method = request.Method;
            Uri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }
}
