using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Payments.Domain;
using Restaurantes.Security;

namespace Restaurantes.Payments.Api.Write.Controllers;

[ApiController, Route("api/payments/orders/{orderId:guid}")]
[Authorize]
public sealed class PaymentsController(
    PaymentCommandService service,
    IPaymentWriteStore store,
    ICustomerDiningSessionValidator dining,
    IPaymentCashRegister cashRegister
) : ControllerBase
{
    private const int TooEarlyStatusCode = 425;

    [AllowAnonymous]
    [HttpPost("capture-customer-qr")]
    public async Task<ActionResult<PaymentResponse>> CaptureCustomerQr(
        Guid orderId,
        CaptureCustomerQrPaymentRequest request,
        CancellationToken ct
    )
    {
        if (request.IdempotencyKey == Guid.Empty || request.DiningSessionId == Guid.Empty)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "IdempotencyKey and DiningSessionId are required",
                    Status = 400,
                }
            );
        }

        PayableOrder? payable = await store.FindAsync(orderId, ct);
        if (payable is null)
        {
            return StatusCode(
                TooEarlyStatusCode,
                new ProblemDetails
                {
                    Title = "Payment projection is not ready",
                    Detail = "The order event is still being projected. Retry this request.",
                    Status = TooEarlyStatusCode,
                }
            );
        }

        try
        {
            await cashRegister.EnsureOpenAsync(payable.RestaurantId, ct);
        }
        catch (PaymentCashRegisterClosedException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Caja cerrada",
                    Detail = e.Message,
                    Status = 409,
                }
            );
        }

        if (
            payable.Source != "CustomerQr"
            || payable.TableId is null
            || payable.DiningSessionId != request.DiningSessionId
        )
        {
            return Forbid();
        }

        string customerAccessToken =
            Request.Headers["X-Customer-Session-Token"].FirstOrDefault() ?? string.Empty;
        if (
            !await dining.IsValidAsync(
                payable.DiningSessionId!.Value,
                payable.RestaurantId,
                payable.TableId.Value,
                payable.ServiceMode,
                customerAccessToken,
                ct
            )
        )
        {
            return Unauthorized(
                new ProblemDetails
                {
                    Title = "Invalid customer dining session",
                    Status = StatusCodes.Status401Unauthorized,
                }
            );
        }

        try
        {
            PaymentResponse? result = await service.CaptureAsync(
                orderId,
                new CapturePaymentRequest
                {
                    IdempotencyKey = request.IdempotencyKey,
                    Method = "Online",
                    ExternalReference = string.IsNullOrWhiteSpace(request.ExternalReference)
                        ? $"QR-{orderId:N}"
                        : request.ExternalReference.Trim(),
                },
                ct
            );
            return result is null ? StatusCode(TooEarlyStatusCode) : Accepted(result);
        }
        catch (InvalidOperationException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Customer QR payment cannot be captured",
                    Detail = e.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
    }

    [HttpPost("capture")]
    public async Task<ActionResult<PaymentResponse>> Capture(
        Guid orderId,
        CapturePaymentRequest request,
        CancellationToken ct
    )
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            return BadRequest(
                new ProblemDetails { Title = "IdempotencyKey is required", Status = 400 }
            );
        }

        PayableOrder? payable = await store.FindAsync(orderId, ct);
        if (payable is null)
        {
            return NotFound();
        }

        if (!User.CanAccessRestaurant(payable.RestaurantId, RestaurantPermissions.PaymentsCapture))
        {
            return Forbid();
        }

        try
        {
            await cashRegister.EnsureOpenAsync(payable.RestaurantId, ct);
        }
        catch (PaymentCashRegisterClosedException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Caja cerrada",
                    Detail = e.Message,
                    Status = 409,
                }
            );
        }

        try
        {
            PaymentResponse? result = await service.CaptureAsync(orderId, request, ct);
            return result is null
                ? NotFound(
                    new ProblemDetails
                    {
                        Title = "Order is not available for payment",
                        Status = 404,
                    }
                )
                : Accepted(result);
        }
        catch (InvalidOperationException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Payment cannot be captured",
                    Detail = e.Message,
                    Status = 409,
                }
            );
        }
    }

    [HttpPost("refund")]
    public async Task<ActionResult<PaymentResponse>> Refund(
        Guid orderId,
        RefundPaymentRequest request,
        CancellationToken ct
    )
    {
        PayableOrder? payable = await store.FindAsync(orderId, ct);
        if (payable is null)
        {
            return NotFound();
        }

        if (!User.CanAccessRestaurant(payable.RestaurantId, RestaurantPermissions.PaymentsRefund))
        {
            return Forbid();
        }

        try
        {
            await cashRegister.EnsureOpenAsync(payable.RestaurantId, ct);
        }
        catch (PaymentCashRegisterClosedException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Caja cerrada",
                    Detail = e.Message,
                    Status = 409,
                }
            );
        }

        try
        {
            PaymentResponse? result = await service.RefundAsync(orderId, request, ct);
            return result is null ? NotFound() : Accepted(result);
        }
        catch (InvalidOperationException e)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Payment cannot be refunded",
                    Detail = e.Message,
                    Status = 409,
                }
            );
        }
    }
}
