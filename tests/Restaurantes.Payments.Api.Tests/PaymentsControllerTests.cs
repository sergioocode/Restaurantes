using Microsoft.AspNetCore.Mvc;
using Restaurantes.Payments.Api.Write.Controllers;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Domain;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentsControllerTests
{
    [Theory]
    [InlineData("customer-session", "customer-session")]
    [InlineData(null, "")]
    public async Task Customer_qr_capture_forwards_header_token_to_session_validation(
        string? header,
        string expectedToken
    )
    {
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        PayableOrder order = PayableOrder.Create(
            orderId,
            restaurantId,
            tableId,
            sessionId,
            "DineIn",
            "CustomerQr",
            12m,
            DateTime.UtcNow
        );
        PaymentStore store = new(order);
        DiningValidator dining = new();
        PaymentsController controller = new(null!, store, dining, new OpenCashRegister());
        CaptureCustomerQrPaymentRequest request = new()
        {
            IdempotencyKey = Guid.NewGuid(),
            DiningSessionId = sessionId,
        };

        ActionResult<Restaurantes.Payments.Contracts.Responses.PaymentResponse> result =
            await controller.CaptureCustomerQr(
                orderId,
                request,
                header,
                TestContext.Current.CancellationToken
            );

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(expectedToken, dining.ReceivedToken);
        Assert.Equal(sessionId, dining.ReceivedSessionId);
    }

    private sealed class PaymentStore(PayableOrder order) : IPaymentWriteStore
    {
        public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<PayableOrder?>(orderId == order.OrderId ? order : null);

        public Task SaveWithEventAsync(
            PayableOrder order,
            object integrationEvent,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class DiningValidator : ICustomerDiningSessionValidator
    {
        public Guid ReceivedSessionId { get; private set; }
        public string? ReceivedToken { get; private set; }

        public Task<bool> IsValidAsync(
            Guid sessionId,
            Guid restaurantId,
            Guid tableId,
            string serviceMode,
            string customerAccessToken,
            CancellationToken cancellationToken
        )
        {
            ReceivedSessionId = sessionId;
            ReceivedToken = customerAccessToken;
            return Task.FromResult(false);
        }
    }

    private sealed class OpenCashRegister : IPaymentCashRegister
    {
        public Task EnsureOpenAsync(Guid restaurantId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
