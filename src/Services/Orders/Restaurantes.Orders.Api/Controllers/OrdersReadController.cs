using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Orders.Api.Security;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersReadController(
    IKitchenOrderReadStore store,
    IOrderSettlementQuery settlements,
    IManagementOrderQuery management,
    IKitchenHistoryQuery history,
    IOrderCashRegister cashRegister,
    IDiningSessionStore dining,
    IConfiguration configuration
) : ControllerBase
{
    [Authorize]
    [HttpGet("kds-history")]
    public async Task<ActionResult<IReadOnlyList<KitchenHistoryResponse>>> KitchenHistory(
        Guid restaurantId,
        string stationCode,
        CancellationToken ct
    )
    {
        if (restaurantId == Guid.Empty || string.IsNullOrWhiteSpace(stationCode))
        {
            return BadRequest();
        }
        if (!User.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse))
        {
            return Forbid();
        }
        DateOnly businessDate = await cashRegister.GetBusinessDateAsync(restaurantId, ct);
        IReadOnlyList<KitchenHistoryResponse>? result = await history.ListAsync(
            restaurantId,
            businessDate,
            stationCode.Trim().ToUpperInvariant(),
            ct
        );
        return result is null ? Forbid() : Ok(result);
    }

    [Authorize]
    [HttpGet("management")]
    public async Task<ActionResult<IReadOnlyList<ManagementOrderResponse>>> ManagementOrders(
        Guid restaurantId,
        DateOnly date,
        CancellationToken ct,
        [FromQuery] int offset = 0
    )
    {
        return restaurantId == Guid.Empty || offset < 0
                ? (ActionResult<IReadOnlyList<ManagementOrderResponse>>)BadRequest()
            : !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.OrdersRead)
            && !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.OrdersReportRead)
                ? (ActionResult<IReadOnlyList<ManagementOrderResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<ManagementOrderResponse>>)
                Ok(await management.ListAsync(restaurantId, date, offset, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/cash-close-orders")]
    public async Task<ActionResult<IReadOnlyList<CashCloseOrderResponse>>> CashCloseOrders(
        Guid restaurantId,
        CancellationToken ct,
        [FromQuery] int offset = 0
    )
    {
        return !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage)
                ? (ActionResult<IReadOnlyList<CashCloseOrderResponse>>)Forbid()
            : offset < 0
                ? (ActionResult<IReadOnlyList<CashCloseOrderResponse>>)
                    BadRequest("offset must be non-negative.")
            : (ActionResult<IReadOnlyList<CashCloseOrderResponse>>)
                Ok(await settlements.ListAsync(restaurantId, offset, ct));
    }

    [Authorize]
    [HttpGet("kds")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<OrderResponse>>> ListKds(
        [FromQuery] Guid restaurantId,
        CancellationToken cancellationToken,
        [FromQuery] string? stationCode = null
    )
    {
        return ListForPermission(
            restaurantId,
            stationCode,
            RestaurantPermissions.KdsUse,
            cancellationToken
        );
    }

    [Authorize]
    [HttpGet("commander")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<OrderResponse>>> ListCommander(
        [FromQuery] Guid restaurantId,
        CancellationToken cancellationToken
    )
    {
        return ListForPermission(
            restaurantId,
            null,
            RestaurantPermissions.CommanderUse,
            cancellationToken,
            nameof(OrderSource.Comandero)
        );
    }

    [Authorize]
    [HttpGet("pos")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<OrderResponse>>> ListPos(
        [FromQuery] Guid restaurantId,
        CancellationToken cancellationToken
    )
    {
        return ListForPermission(
            restaurantId,
            null,
            RestaurantPermissions.PosUse,
            cancellationToken,
            nameof(OrderSource.Pos),
            nameof(OrderSource.CustomerQr)
        );
    }

    private async Task<ActionResult<IReadOnlyList<OrderResponse>>> ListForPermission(
        Guid restaurantId,
        string? stationCode,
        string permission,
        CancellationToken cancellationToken,
        params string[] allowedSources
    )
    {
        if (restaurantId == Guid.Empty)
        {
            return BadRequest("restaurantId is required.");
        }
        if (!User.CanAccessRestaurant(restaurantId, permission))
        {
            return Forbid();
        }

        IReadOnlyList<OrderResponse> orders = await store.ListAsync(
            restaurantId,
            stationCode,
            cancellationToken
        );
        return Ok(
            allowedSources.Length == 0
                ? orders
                : orders.Where(order => allowedSources.Contains(order.Source)).ToArray()
        );
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(
        Guid id,
        [FromHeader(Name = "X-Customer-Session-Token")] string? customerAccessToken,
        CancellationToken cancellationToken
    )
    {
        OrderResponse? order = await store.FindAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        bool canManageOrder =
            User.Identity?.IsAuthenticated == true
            && User.CanManageOrder(order.RestaurantId, order.Source);
        if (order.Source == "CustomerQr" && !canManageOrder)
        {
            ObjectResult? denied = QrNetworkAccess.DeniedResult(HttpContext, configuration);
            if (denied is not null)
            {
                return denied;
            }
            if (order.DiningSessionId is null || order.TableId is null)
            {
                return Forbid();
            }

            await dining.EnsureOpenAsync(
                order.DiningSessionId.Value,
                order.RestaurantId,
                order.TableId.Value,
                order.Source,
                order.ServiceMode,
                customerAccessToken ?? string.Empty,
                cancellationToken
            );
        }
        else if (!canManageOrder)
        {
            return Forbid();
        }

        return Ok(order);
    }
}
