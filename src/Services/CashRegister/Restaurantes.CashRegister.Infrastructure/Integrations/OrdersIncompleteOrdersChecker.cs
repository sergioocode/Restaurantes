using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.CashRegister.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;

namespace Restaurantes.CashRegister.Infrastructure.Integrations;

public sealed class OrdersIncompleteOrdersChecker(HttpClient orders, IHttpClientFactory clients)
    : IIncompleteOrdersChecker
{
    public async Task<int> CountAsync(Guid restaurantId, string authorization, CancellationToken ct)
    {
        try
        {
            if (
                !AuthenticationHeaderValue.TryParse(
                    authorization,
                    out AuthenticationHeaderValue? bearer
                )
            )
            {
                throw new InvalidOperationException("The authorization header is missing.");
            }
            using HttpClient payments = clients.CreateClient("CashClosePayments");
            int offset = 0;
            int pending = 0;
            while (true)
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    $"api/orders/restaurants/{restaurantId}/cash-close-orders?offset={offset}"
                );
                request.Headers.Authorization = bearer;
                using HttpResponseMessage response = await orders.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                CashCloseOrder[] batch =
                    await response.Content.ReadFromJsonAsync<CashCloseOrder[]>(
                        cancellationToken: ct
                    ) ?? throw new InvalidOperationException("Orders returned an empty response.");
                if (batch.Length == 0)
                {
                    return pending;
                }
                using HttpRequestMessage paymentRequest = new(
                    HttpMethod.Post,
                    $"api/payments/restaurants/{restaurantId}/order-statuses"
                );
                paymentRequest.Headers.Authorization = bearer;
                paymentRequest.Content = JsonContent.Create(
                    new OrderPaymentStatusesRequest
                    {
                        OrderIds = batch.Select(order => order.OrderId).ToArray(),
                    }
                );
                using HttpResponseMessage paymentResponse = await payments.SendAsync(
                    paymentRequest,
                    ct
                );
                paymentResponse.EnsureSuccessStatusCode();
                OrderPaymentStatusResponse[] statuses =
                    await paymentResponse.Content.ReadFromJsonAsync<OrderPaymentStatusResponse[]>(
                        cancellationToken: ct
                    )
                    ?? throw new InvalidOperationException("Payments returned an empty response.");
                Dictionary<Guid, string> byOrder = statuses.ToDictionary(
                    payment => payment.OrderId,
                    payment => payment.Status
                );
                // Kitchen status never decides whether a non-cancelled order has been paid.
                pending += batch.Count(order =>
                    !order.IsCancelled && byOrder.GetValueOrDefault(order.OrderId) != "Paid"
                );
                if (batch.Length < 500)
                {
                    return pending;
                }
                offset += batch.Length;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or TaskCanceledException
                        or InvalidOperationException
                        or JsonException
                        or ArgumentException
            )
        {
            throw new IncompleteOrdersUnavailableException(exception);
        }
    }

    private sealed record CashCloseOrder(Guid OrderId, bool IsCancelled);
}
