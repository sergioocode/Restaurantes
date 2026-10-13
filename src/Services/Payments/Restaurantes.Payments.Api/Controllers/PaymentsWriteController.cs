using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Payments.Api.Controllers;

[ApiController, Route("api/payments/orders/{orderId:guid}")]
[Authorize]
public sealed class PaymentsWriteController(
    PaymentCommandService service,
    IPaymentWriteStore store,
    ICustomerDiningSessionValidator dining,
    IPaymentCashRegister cashRegister,
    PaymentWriteDbContext db,
    IConfiguration configuration
) : ControllerBase
{
    private const int TooEarlyStatusCode = 425;

    [HttpPut("amount")]
    public async Task<IActionResult> AdjustAmount(
        Guid orderId,
        SetPayableOrderAmountRequest request,
        CancellationToken ct
    )
    {
        PayableOrder? payable = await store.FindAsync(orderId, ct);
        if (payable is null)
        {
            return NotFound();
        }
        if (
            payable.DiningSessionId is null
            || !User.CanAccessRestaurant(payable.RestaurantId, RestaurantPermissions.DiscountsApply)
        )
        {
            return Forbid();
        }
        try
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(
                ct
            );
            await db.Entry(payable).ReloadAsync(ct);
            bool updated = await service.AdjustAmountAsync(orderId, request.Amount, ct);
            await transaction.CommitAsync(ct);
            return updated ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = exception.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
    }

    [AllowAnonymous]
    [HttpPost("capture-customer-qr")]
    public async Task<ActionResult<PaymentResponse>> CaptureCustomerQr(
        Guid orderId,
        CaptureCustomerQrPaymentRequest request,
        [FromHeader(Name = "X-Customer-Session-Token")] string? customerAccessToken,
        CancellationToken ct
    )
    {
        ObjectResult? denied = QrNetworkAccess.DeniedResult(HttpContext, configuration);
        if (denied is not null)
        {
            return denied;
        }

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

        if (
            payable.Source != "CustomerQr"
            || payable.TableId is null
            || payable.DiningSessionId != request.DiningSessionId
        )
        {
            return Forbid();
        }

        if (
            !await dining.IsValidAsync(
                payable.DiningSessionId!.Value,
                payable.RestaurantId,
                payable.TableId.Value,
                payable.ServiceMode,
                customerAccessToken ?? string.Empty,
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
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(
                ct
            );
            CashRegisterSessionDetails cashSession = await cashRegister.GetOpenSessionAsync(
                payable.RestaurantId,
                ct
            );
            await db.Entry(payable).ReloadAsync(ct);
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
                cashSession,
                ct
            );
            await transaction.CommitAsync(ct);
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
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(
                ct
            );
            CashRegisterSessionDetails cashSession = await cashRegister.GetOpenSessionAsync(
                payable.RestaurantId,
                ct
            );
            await db.Entry(payable).ReloadAsync(ct);
            PaymentResponse? result = await service.CaptureAsync(orderId, request, cashSession, ct);
            await transaction.CommitAsync(ct);
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
            await cashRegister.GetOpenSessionAsync(payable.RestaurantId, ct);
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
