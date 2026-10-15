using System.Net;
using System.Net.Http.Json;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Integrations;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class OrdersIncompleteOrdersCheckerTests
{
    [Theory]
    [InlineData(false, "Paid", 0)]
    [InlineData(false, "Open", 1)]
    [InlineData(false, null, 1)]
    [InlineData(true, "Paid", 0)]
    [InlineData(true, "Open", 0)]
    [InlineData(true, null, 0)]
    public async Task Close_uses_actual_payment_or_cancellation_instead_of_kitchen_status(
        bool cancelled,
        string? paymentStatus,
        int expected
    )
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        using Handler orders = new(request =>
        {
            Assert.Contains(
                $"/restaurants/{restaurantId}/cash-close-orders?offset=0",
                request.RequestUri!.ToString()
            );
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            return Json(new[] { new { orderId, isCancelled = cancelled } });
        });
        using Handler payments = new(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains(
                $"/restaurants/{restaurantId}/order-statuses",
                request.RequestUri!.ToString()
            );
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            return Json(
                paymentStatus is null ? [] : new[] { new { orderId, status = paymentStatus } }
            );
        });
        using HttpClient ordersClient = Client(orders);
        OrdersIncompleteOrdersChecker checker = new(ordersClient, new ClientFactory(payments));

        Assert.Equal(
            expected,
            await checker.CountAsync(
                restaurantId,
                "Bearer test-token",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, payments.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unavailable_service_does_not_turn_into_zero_pending_orders(bool failOrders)
    {
        using Handler orders = new(_ =>
            failOrders
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(new[] { new { orderId = Guid.NewGuid(), isCancelled = false } })
        );
        using Handler payments = new(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable
        ));
        using HttpClient ordersClient = Client(orders);
        OrdersIncompleteOrdersChecker checker = new(ordersClient, new ClientFactory(payments));

        await Assert.ThrowsAsync<IncompleteOrdersUnavailableException>(() =>
            checker.CountAsync(
                Guid.NewGuid(),
                "Bearer test-token",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Empty_orders_do_not_require_a_payment_lookup()
    {
        using Handler orders = new(_ => Json(Array.Empty<object>()));
        using Handler payments = new(_ =>
            throw new InvalidOperationException("Unexpected payment lookup")
        );
        using HttpClient ordersClient = Client(orders);
        OrdersIncompleteOrdersChecker checker = new(ordersClient, new ClientFactory(payments));

        Assert.Equal(
            0,
            await checker.CountAsync(
                Guid.NewGuid(),
                "Bearer test-token",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(0, payments.Calls);
    }

    [Fact]
    public async Task Pagination_counts_an_unpaid_order_after_the_first_batch()
    {
        Guid[] ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();
        using Handler orders = new(request =>
            Json(
                (request.RequestUri!.Query == "?offset=0" ? ids.Take(500) : ids.Skip(500))
                    .Select(id => new { orderId = id, isCancelled = false })
                    .ToArray()
            )
        );
        using Handler payments = new(_ =>
            Json(ids.Take(500).Select(id => new { orderId = id, status = "Paid" }).ToArray())
        );
        using HttpClient ordersClient = Client(orders);
        OrdersIncompleteOrdersChecker checker = new(ordersClient, new ClientFactory(payments));

        Assert.Equal(
            1,
            await checker.CountAsync(
                Guid.NewGuid(),
                "Bearer test-token",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(2, orders.Calls);
        Assert.Equal(2, payments.Calls);
    }

    private static HttpClient Client(HttpMessageHandler handler)
    {
        return new(handler, false) { BaseAddress = new Uri("http://services.test/") };
    }

    private static HttpResponseMessage Json(object value)
    {
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("CashClosePayments", name);
            return Client(handler);
        }
    }
}
