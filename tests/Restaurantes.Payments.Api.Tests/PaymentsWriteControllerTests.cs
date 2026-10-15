using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Payments.Api.Controllers;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Domain;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentsWriteControllerTests
{
    [Fact]
    public void Adjust_amount_request_accepts_decimal_amount_with_spanish_culture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo spanishCulture = CultureInfo.GetCultureInfo("es-ES");
            CultureInfo.CurrentCulture = spanishCulture;
            CultureInfo.CurrentUICulture = spanishCulture;

            SetPayableOrderAmountRequest request = new() { Amount = 7.13m };
            List<ValidationResult> validationResults = [];
            bool isValid = Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                validationResults,
                validateAllProperties: true
            );

            Assert.True(isValid);
            Assert.Empty(validationResults);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

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
        PaymentsWriteController controller = new(
            null!,
            store,
            dining,
            new OpenCashRegister(),
            null!,
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["QrAccess:AllowedClientNetworks:0"] = "127.0.0.1",
                    }
                )
                .Build()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        CaptureCustomerQrPaymentRequest request = new()
        {
            IdempotencyKey = Guid.NewGuid(),
            DiningSessionId = sessionId,
        };

        ActionResult<Contracts.Responses.PaymentResponse> result =
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
        public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PayableOrder?>(orderId == order.OrderId ? order : null);
        }

        public Task<PayableOrderCapture?> FindCaptureAsync(
            Guid orderId,
            Guid idempotencyKey,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<PayableOrderCapture?>(null);
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveWithEventAsync(
            PayableOrder order,
            PayableOrderCapture capture,
            object integrationEvent,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task SaveWithEventAsync(
            PayableOrder order,
            object integrationEvent,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }
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

        public Task<bool> IsChargeAmountValidAsync(
            Guid sessionId,
            Guid orderId,
            Guid restaurantId,
            decimal amount,
            string? authorization,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }

    private sealed class OpenCashRegister : IPaymentCashRegister
    {
        public Task<CashRegisterSessionDetails> GetOpenSessionAsync(
            Guid restaurantId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new CashRegisterSessionDetails(Guid.NewGuid(), new DateOnly(2026, 10, 4))
            );
        }
    }
}
