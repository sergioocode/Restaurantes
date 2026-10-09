using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Orders.Api.Security;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersWriteController(
    OrderCommandService commandService,
    IOrderWriteStore store,
    IDiningSessionStore dining,
    IOrderCashRegister cashRegister,
    OrderWriteDbContext db,
    IConfiguration configuration
) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "X-Customer-Session-Token")] string? customerAccessToken,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.Equals(request.Source, nameof(OrderSource.CustomerQr), StringComparison.Ordinal)
        )
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }

            string? clientPermission = OrderClientAccess.PermissionForSource(request.Source);
            if (
                clientPermission is null
                || !User.CanAccessRestaurant(
                    request.RestaurantId,
                    RestaurantPermissions.OrdersCreate
                )
                || !User.CanAccessRestaurant(request.RestaurantId, clientPermission)
            )
            {
                return Forbid();
            }
        }
        else
        {
            ObjectResult? denied = QrNetworkAccess.DeniedResult(HttpContext, configuration);
            if (denied is not null)
            {
                return denied;
            }
        }
        try
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            OrderCashRegisterSession cashSession = await cashRegister.EnsureOpenAsync(
                request.RestaurantId,
                cancellationToken
            );
            OrderResponse order = await commandService.CreateAsync(
                request,
                customerAccessToken ?? string.Empty,
                cashSession,
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
            return Accepted($"/api/orders/{order.Id}", order);
        }
        catch (OrderCashRegisterClosedException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Caja cerrada",
                    Detail = exception.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Catalog products are unavailable",
                    Detail = exception.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
        catch (ArgumentException exception)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid order",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }
    }

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Submit(
        Guid id,
        [FromHeader(Name = "X-Customer-Session-Token")] string? customerAccessToken,
        CancellationToken cancellationToken
    )
    {
        Order? existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Source != OrderSource.CustomerQr)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }

            if (!User.CanManageOrder(existing.RestaurantId, existing.Source.ToString()))
            {
                return Forbid();
            }
        }
        else
        {
            ObjectResult? denied = QrNetworkAccess.DeniedResult(HttpContext, configuration);
            if (denied is not null)
            {
                return denied;
            }

            await dining.EnsureOpenAsync(
                existing.DiningSessionId!.Value,
                existing.RestaurantId,
                existing.TableId!.Value,
                nameof(OrderSource.CustomerQr),
                nameof(ServiceMode.DineIn),
                customerAccessToken ?? string.Empty,
                cancellationToken
            );
        }
        try
        {
            await cashRegister.EnsureOpenAsync(existing.RestaurantId, cancellationToken);
            OrderResponse? order = await commandService.SubmitAsync(id, cancellationToken);
            return order is null ? NotFound() : Accepted($"/api/orders/{order.Id}", order);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Order cannot be submitted",
                    Detail = exception.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
    }

    [HttpPost("{id:guid}/lines/{lineId:guid}/start-preparation")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> StartPreparation(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.StartLinePreparationAsync(orderId, lineId, token),
            "Order cannot start preparation",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/lines/{lineId:guid}/ready")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> MarkReady(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.MarkLineReadyAsync(orderId, lineId, token),
            "Order cannot be marked as ready",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/lines/{lineId:guid}/dispatch")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> DispatchStation(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.DispatchLineAsync(orderId, lineId, token),
            "Kitchen pass cannot be dispatched",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/deliver")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> Deliver(Guid id, CancellationToken cancellationToken)
    {
        return ExecuteOrderManagementTransitionAsync(
            id,
            commandService.DeliverAsync,
            "Order cannot be delivered",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> Cancel(
        Guid id,
        CancelOrderRequest request,
        CancellationToken cancellationToken
    )
    {
        string actor =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        return ExecuteOrderManagementTransitionAsync(
            id,
            (orderId, token) => commandService.CancelAsync(orderId, request.Reason, actor, token),
            "Order cannot be cancelled",
            cancellationToken,
            requiresOpenCashRegister: true
        );
    }

    [HttpPost("{id:guid}/lines/{lineId:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> CancelLine(
        Guid id,
        Guid lineId,
        CancelOrderLineRequest request,
        CancellationToken cancellationToken
    )
    {
        Order? existing = await store.FindAsync(id, cancellationToken);
        return existing is null ? (ActionResult<OrderResponse>)NotFound()
            : User.Identity?.IsAuthenticated != true ? (ActionResult<OrderResponse>)Unauthorized()
            : !User.CanManageOrder(existing.RestaurantId, existing.Source.ToString())
                ? (ActionResult<OrderResponse>)Forbid()
            : await ExecuteTransitionAsync(
                id,
                (orderId, token) =>
                    commandService.CancelLineAsync(orderId, lineId, request.Reason, token),
                "Order line cannot be cancelled",
                cancellationToken,
                existing.RestaurantId
            );
    }

    private async Task<ActionResult<OrderResponse>> ExecuteTransitionAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<OrderResponse?>> transition,
        string conflictTitle,
        CancellationToken cancellationToken,
        Guid? cashRestaurantId = null
    )
    {
        try
        {
            await using IDbContextTransaction? transaction = cashRestaurantId.HasValue
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            if (transaction is not null)
            {
                await cashRegister.EnsureOpenAsync(cashRestaurantId.Value, cancellationToken);
                db.ChangeTracker.Clear();
            }
            OrderResponse? order = await transition(id, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return order is null ? NotFound() : Accepted($"/api/orders/{order.Id}", order);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = conflictTitle,
                    Detail = exception.Message,
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
        catch (ArgumentException exception)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid order transition",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Order changed concurrently",
                    Detail =
                        "Another KDS changed this order. Refresh the projection and retry the command.",
                    Status = StatusCodes.Status409Conflict,
                }
            );
        }
    }

    private async Task<ActionResult<OrderResponse>> ExecuteAuthorizedTransitionAsync(
        Guid id,
        string permission,
        Func<Guid, CancellationToken, Task<OrderResponse?>> transition,
        string conflictTitle,
        CancellationToken cancellationToken,
        bool requiresOpenCashRegister = false
    )
    {
        Order? existing = await store.FindAsync(id, cancellationToken);
        return existing is null ? (ActionResult<OrderResponse>)NotFound()
            : User.Identity?.IsAuthenticated != true ? (ActionResult<OrderResponse>)Unauthorized()
            : !User.CanAccessRestaurant(existing.RestaurantId, permission)
                ? (ActionResult<OrderResponse>)Forbid()
            : await ExecuteTransitionAsync(
                id,
                transition,
                conflictTitle,
                cancellationToken,
                requiresOpenCashRegister ? existing.RestaurantId : null
            );
    }

    private async Task<ActionResult<OrderResponse>> ExecuteOrderManagementTransitionAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<OrderResponse?>> transition,
        string conflictTitle,
        CancellationToken cancellationToken,
        bool requiresOpenCashRegister = false
    )
    {
        Order? existing = await store.FindAsync(id, cancellationToken);
        return existing is null ? (ActionResult<OrderResponse>)NotFound()
            : User.Identity?.IsAuthenticated != true ? (ActionResult<OrderResponse>)Unauthorized()
            : !User.CanManageOrder(existing.RestaurantId, existing.Source.ToString())
                ? (ActionResult<OrderResponse>)Forbid()
            : await ExecuteTransitionAsync(
                id,
                transition,
                conflictTitle,
                cancellationToken,
                requiresOpenCashRegister ? existing.RestaurantId : null
            );
    }
}
