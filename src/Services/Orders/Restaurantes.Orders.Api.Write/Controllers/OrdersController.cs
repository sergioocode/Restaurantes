using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Domain;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Write.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    OrderCommandService commandService,
    IOrderWriteStore store,
    IDiningSessionStore dining,
    CashRegisterAvailabilityClient cashRegister
) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
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

            if (!User.CanAccessRestaurant(request.RestaurantId, RestaurantPermissions.OrdersCreate))
            {
                return Forbid();
            }
        }
        try
        {
            await cashRegister.EnsureOpenAsync(request.RestaurantId, cancellationToken);
            string customerAccessToken =
                Request.Headers["X-Customer-Session-Token"].FirstOrDefault() ?? string.Empty;
            OrderResponse order = await commandService.CreateAsync(
                request,
                customerAccessToken,
                cancellationToken
            );
            return Accepted($"/api/orders/{order.Id}", order);
        }
        catch (CashRegisterClosedException exception)
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

            if (
                !User.CanAccessRestaurant(existing.RestaurantId, RestaurantPermissions.OrdersManage)
            )
            {
                return Forbid();
            }
        }
        else
        {
            string customerAccessToken =
                Request.Headers["X-Customer-Session-Token"].FirstOrDefault() ?? string.Empty;
            await dining.EnsureOpenAsync(
                existing.DiningSessionId!.Value,
                existing.RestaurantId,
                existing.TableId!.Value,
                nameof(OrderSource.CustomerQr),
                nameof(ServiceMode.DineIn),
                customerAccessToken,
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

    [HttpPost("{id:guid}/stations/{stationCode}/start-preparation")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> StartPreparation(
        Guid id,
        string stationCode,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.StartPreparationAsync(orderId, stationCode, token),
            "Order cannot start preparation",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/stations/{stationCode}/ready")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> MarkReady(
        Guid id,
        string stationCode,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.MarkReadyAsync(orderId, stationCode, token),
            "Order cannot be marked as ready",
            cancellationToken
        );
    }

    [HttpPost("{id:guid}/stations/{stationCode}/dispatch")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OrderResponse>> DispatchStation(
        Guid id,
        string stationCode,
        CancellationToken cancellationToken
    )
    {
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.KdsUse,
            (orderId, token) => commandService.DispatchStationAsync(orderId, stationCode, token),
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
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.OrdersManage,
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
        return ExecuteAuthorizedTransitionAsync(
            id,
            RestaurantPermissions.OrdersRecover,
            (orderId, token) =>
                commandService.CancelTakeawayAsync(orderId, request.Reason, actor, token),
            "Order cannot be cancelled",
            cancellationToken
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
            : !User.CanAccessRestaurant(existing.RestaurantId, RestaurantPermissions.OrdersManage)
                ? (ActionResult<OrderResponse>)Forbid()
            : await ExecuteTransitionAsync(
                id,
                (orderId, token) =>
                    commandService.CancelLineAsync(orderId, lineId, request.Reason, token),
                "Order line cannot be cancelled",
                cancellationToken
            );
    }

    private async Task<ActionResult<OrderResponse>> ExecuteTransitionAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<OrderResponse?>> transition,
        string conflictTitle,
        CancellationToken cancellationToken
    )
    {
        try
        {
            OrderResponse? order = await transition(id, cancellationToken);
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
        CancellationToken cancellationToken
    )
    {
        Order? existing = await store.FindAsync(id, cancellationToken);
        return existing is null ? (ActionResult<OrderResponse>)NotFound()
            : User.Identity?.IsAuthenticated != true ? (ActionResult<OrderResponse>)Unauthorized()
            : !User.CanAccessRestaurant(existing.RestaurantId, permission)
                ? (ActionResult<OrderResponse>)Forbid()
            : await ExecuteTransitionAsync(id, transition, conflictTitle, cancellationToken);
    }
}
