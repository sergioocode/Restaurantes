using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Payments.Api.Controllers;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Domain;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentsWriteControllerTests
{
    [Fact]
    public async Task Capture_rejects_empty_idempotency_key_before_loading_order()
    {
        PaymentStore store = new(null);
        PaymentsWriteController controller = CreateController(store);

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Capture(
            Guid.NewGuid(),
            new CapturePaymentRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, store.FindCount);
    }

    [Fact]
    public async Task Capture_returns_not_found_when_order_projection_is_missing()
    {
        PaymentsWriteController controller = CreateController(new PaymentStore(null));

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Capture(
            Guid.NewGuid(),
            new CapturePaymentRequest { IdempotencyKey = Guid.NewGuid() },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Adjust_amount_returns_not_found_when_order_projection_is_missing()
    {
        PaymentsWriteController controller = CreateController(new PaymentStore(null));

        IActionResult result = await controller.AdjustAmount(
            Guid.NewGuid(),
            new SetPayableOrderAmountRequest { Amount = 9m },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Adjust_amount_requires_permission_even_when_order_has_a_session()
    {
        PayableOrder order = CreatePayableOrder(diningSessionId: Guid.NewGuid());
        PaymentsWriteController controller = CreateController(new PaymentStore(order));

        IActionResult result = await controller.AdjustAmount(
            order.OrderId,
            new SetPayableOrderAmountRequest
            {
                Amount = 9m,
                DiningSessionId = order.DiningSessionId,
            },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Adjust_amount_requires_valid_dining_session_for_card_permission()
    {
        PayableOrder order = CreatePayableOrder(diningSessionId: Guid.NewGuid());
        PaymentsWriteController controller = CreateController(
            new PaymentStore(order),
            order.RestaurantId,
            RestaurantPermissions.PaymentsCaptureCard
        );

        IActionResult result = await controller.AdjustAmount(
            order.OrderId,
            new SetPayableOrderAmountRequest
            {
                Amount = 9m,
                DiningSessionId = order.DiningSessionId,
            },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Capture_forbids_without_scoped_payment_permission()
    {
        PayableOrder order = CreatePayableOrder();
        PaymentsWriteController controller = CreateController(new PaymentStore(order));

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Capture(
            order.OrderId,
            new CapturePaymentRequest { IdempotencyKey = Guid.NewGuid() },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Adjust_amount_forbids_when_order_is_not_attached_to_a_dining_session()
    {
        PayableOrder order = CreatePayableOrder(diningSessionId: null);
        PaymentsWriteController controller = CreateController(new PaymentStore(order));

        IActionResult result = await controller.AdjustAmount(
            order.OrderId,
            new SetPayableOrderAmountRequest { Amount = 9m },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Refund_forbids_without_scoped_refund_permission()
    {
        PayableOrder order = CreatePayableOrder();
        PaymentsWriteController controller = CreateController(new PaymentStore(order));

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Refund(
            order.OrderId,
            new RefundPaymentRequest { Reason = "Solicitud del cliente" },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Refund_returns_not_found_when_order_projection_is_missing()
    {
        PaymentsWriteController controller = CreateController(new PaymentStore(null));

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Refund(
            Guid.NewGuid(),
            new RefundPaymentRequest { Reason = "Solicitud del cliente" },
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Customer_qr_capture_rejects_a_client_network_that_is_not_allowed()
    {
        PaymentStore store = new(null);
        PaymentsWriteController controller = CreateQrController(store, allowedNetworks: []);

        ActionResult<Contracts.Responses.PaymentResponse> result =
            await controller.CaptureCustomerQr(
                Guid.NewGuid(),
                new CaptureCustomerQrPaymentRequest
                {
                    IdempotencyKey = Guid.NewGuid(),
                    DiningSessionId = Guid.NewGuid(),
                },
                null,
                TestContext.Current.CancellationToken
            );

        ObjectResult denied = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Equal(0, store.FindCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Customer_qr_capture_requires_idempotency_and_session_ids(
        bool hasIdempotencyKey,
        bool hasSessionId
    )
    {
        PaymentStore store = new(null);
        PaymentsWriteController controller = CreateQrController(store, ["127.0.0.1"]);

        ActionResult<Contracts.Responses.PaymentResponse> result =
            await controller.CaptureCustomerQr(
                Guid.NewGuid(),
                new CaptureCustomerQrPaymentRequest
                {
                    IdempotencyKey = hasIdempotencyKey ? Guid.NewGuid() : Guid.Empty,
                    DiningSessionId = hasSessionId ? Guid.NewGuid() : Guid.Empty,
                },
                null,
                TestContext.Current.CancellationToken
            );

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, store.FindCount);
    }

    [Fact]
    public async Task Customer_qr_capture_returns_too_early_when_order_projection_is_missing()
    {
        PaymentStore store = new(null);
        PaymentsWriteController controller = CreateQrController(store, ["127.0.0.1"]);

        ActionResult<Contracts.Responses.PaymentResponse> result =
            await controller.CaptureCustomerQr(
                Guid.NewGuid(),
                new CaptureCustomerQrPaymentRequest
                {
                    IdempotencyKey = Guid.NewGuid(),
                    DiningSessionId = Guid.NewGuid(),
                },
                null,
                TestContext.Current.CancellationToken
            );

        ObjectResult tooEarly = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(425, tooEarly.StatusCode);
        Assert.Equal(1, store.FindCount);
    }

    [Theory]
    [InlineData("Pos", true, false)]
    [InlineData("CustomerQr", false, false)]
    [InlineData("CustomerQr", true, true)]
    public async Task Customer_qr_capture_forbids_orders_with_incompatible_source_or_session(
        string source,
        bool hasTable,
        bool useWrongSession
    )
    {
        Guid sessionId = Guid.NewGuid();
        PayableOrder order = PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            hasTable ? Guid.NewGuid() : null,
            sessionId,
            "DineIn",
            source,
            12m,
            DateTime.UtcNow
        );
        PaymentsWriteController controller = CreateQrController(
            new PaymentStore(order),
            ["127.0.0.1"]
        );

        ActionResult<Contracts.Responses.PaymentResponse> result =
            await controller.CaptureCustomerQr(
                order.OrderId,
                new CaptureCustomerQrPaymentRequest
                {
                    IdempotencyKey = Guid.NewGuid(),
                    DiningSessionId = useWrongSession ? Guid.NewGuid() : sessionId,
                },
                null,
                TestContext.Current.CancellationToken
            );

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Refund_returns_conflict_when_cash_register_is_closed()
    {
        PayableOrder order = CreatePayableOrder();
        PaymentsWriteController controller = CreateController(
            new PaymentStore(order),
            order.RestaurantId,
            RestaurantPermissions.PaymentsRefund,
            new ClosedCashRegister()
        );

        ActionResult<Contracts.Responses.PaymentResponse> result = await controller.Refund(
            order.OrderId,
            new RefundPaymentRequest { Reason = "Solicitud del cliente" },
            TestContext.Current.CancellationToken
        );

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        ProblemDetails details = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Caja cerrada", details.Title);
        Assert.Equal(StatusCodes.Status409Conflict, details.Status);
    }

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

    private static PaymentsWriteController CreateController(
        PaymentStore store,
        Guid? authorizedRestaurantId = null,
        string? permission = null,
        IPaymentCashRegister? cashRegister = null
    )
    {
        PaymentsWriteController controller = new(
            null!,
            store,
            new DiningValidator(),
            cashRegister ?? new OpenCashRegister(),
            null!,
            new ConfigurationBuilder().Build()
        );
        ClaimsIdentity identity = new(
            authorizedRestaurantId is Guid restaurantId && permission is not null
                ?
                [
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(restaurantId, permission)
                    ),
                ]
                : [],
            "Test"
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static PaymentsWriteController CreateQrController(
        PaymentStore store,
        string[] allowedNetworks
    )
    {
        PaymentsWriteController controller = new(
            null!,
            store,
            new DiningValidator(),
            new OpenCashRegister(),
            null!,
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    allowedNetworks.Select(
                        (network, index) =>
                            new KeyValuePair<string, string?>(
                                $"QrAccess:AllowedClientNetworks:{index}",
                                network
                            )
                    )
                )
                .Build()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        return controller;
    }

    private static PayableOrder CreatePayableOrder(Guid? diningSessionId = null)
    {
        return PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            diningSessionId,
            "DineIn",
            "Pos",
            12m,
            DateTime.UtcNow
        );
    }

    private sealed class PaymentStore(PayableOrder? order) : IPaymentWriteStore
    {
        public int FindCount { get; private set; }

        public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken)
        {
            FindCount++;
            return Task.FromResult<PayableOrder?>(orderId == order?.OrderId ? order : null);
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

    private sealed class ClosedCashRegister : IPaymentCashRegister
    {
        public Task<CashRegisterSessionDetails> GetOpenSessionAsync(
            Guid restaurantId,
            CancellationToken cancellationToken
        )
        {
            throw new PaymentCashRegisterClosedException(restaurantId);
        }
    }
}
