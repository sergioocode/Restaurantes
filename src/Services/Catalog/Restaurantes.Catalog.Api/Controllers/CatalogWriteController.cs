using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Catalog.Api.Realtime;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Controllers;

[ApiController, Authorize, Route("api/catalog")]
public sealed class CatalogWriteController(
    CatalogCommandService service,
    CatalogMenuSynchronizer synchronizer,
    CatalogNotifications notifications,
    CatalogWriteDbContext db
) : ControllerBase
{
    [HttpPost("categories")]
    public async Task<ActionResult<CategoryResponse>> CreateCategory(
        CreateCategoryRequest request,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }
        CategoryResponse? item = await service.CreateCategoryAsync(request, ct);
        return item is null
            ? Conflict(new ProblemDetails { Title = "Category code already exists", Status = 409 })
            : Accepted($"/api/catalog/categories/{item.Id}", item);
    }

    [HttpPost("products")]
    public async Task<ActionResult<ProductResponse>> CreateProduct(
        CreateProductRequest request,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }
        ProductResponse? item = await service.CreateProductAsync(request, ct);
        return item is null
            ? Conflict(
                new ProblemDetails
                {
                    Title = "SKU already exists or category does not exist",
                    Status = 409,
                }
            )
            : Accepted($"/api/catalog/products/{item.Id}", item);
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> UpdateCategory(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        CategoryResponse? item = await service.UpdateCategoryAsync(id, request, ct);
        if (item is null)
            return NotFound();
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return Accepted($"/api/catalog/categories/{id}", item);
    }

    [HttpPut("products/{id:guid}")]
    public async Task<ActionResult<ProductResponse>> UpdateProduct(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ProductResponse? item = await service.UpdateProductAsync(id, request, ct);
        if (item is null)
            return NotFound();
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        await notifications.MenuChanged(Guid.Empty);
        return Accepted($"/api/catalog/products/{id}", item);
    }

    [HttpPut("products/{id:guid}/category")]
    public async Task<ActionResult<ProductResponse>> SetProductCategory(
        Guid id,
        [FromBody] Guid categoryId,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ProductResponse? item = await service.SetProductCategoryAsync(id, categoryId, ct);
        if (item is null)
            return NotFound();
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return Accepted($"/api/catalog/products/{id}", item);
    }

    [HttpPut("products/{id:guid}/name")]
    public async Task<ActionResult<ProductResponse>> SetProductName(
        Guid id,
        [FromBody] string name,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ProductResponse? item = await service.SetProductNameAsync(id, name, ct);
        if (item is null)
            return NotFound();
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return Accepted($"/api/catalog/products/{id}", item);
    }

    [HttpPut("products/{id:guid}/active")]
    public async Task<ActionResult<ProductResponse>> SetProductActive(
        Guid id,
        [FromBody] bool active,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ProductResponse? item = await service.SetProductActiveAsync(id, active, ct);
        if (item is null)
            return NotFound();
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        await notifications.MenuChanged(Guid.Empty);
        return Accepted($"/api/catalog/products/{id}", item);
    }

    [HttpPut("restaurants/{restaurantId:guid}/products/{productId:guid}")]
    public async Task<ActionResult<MenuItemResponse>> Configure(
        Guid restaurantId,
        Guid productId,
        ConfigureMenuItemRequest request,
        CancellationToken ct
    )
    {
        if (!User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage))
        {
            return Forbid();
        }
        MenuItemResponse? item = await service.ConfigureMenuItemAsync(
            restaurantId,
            productId,
            request,
            ct
        );
        if (item is null)
            return NotFound(
                new ProblemDetails
                {
                    Title = "Product or category was not found or is inactive",
                    Status = 404,
                }
            );
        await synchronizer.SyncAffectedMenus(restaurantId, ct);
        await notifications.MenuChanged(restaurantId);
        return Accepted($"/api/catalog/restaurants/{restaurantId}/menu", item);
    }

    [HttpPost("restaurants/{restaurantId:guid}/stations")]
    public async Task<ActionResult<KitchenStationResponse>> CreateStation(
        Guid restaurantId,
        CreateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        if (!CanManageStation(restaurantId))
        {
            return Forbid();
        }

        KitchenStationResponse? item = await service.CreateStationAsync(restaurantId, request, ct);
        if (item is not null)
            await synchronizer.SyncAffectedMenus(restaurantId, ct);
        return item is null
            ? Conflict(
                new ProblemDetails
                {
                    Title = "Station code already exists for this restaurant",
                    Status = 409,
                }
            )
            : Accepted($"/api/catalog/restaurants/{restaurantId}/stations", item);
    }

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}")]
    public async Task<ActionResult<KitchenStationResponse>> UpdateStation(
        Guid restaurantId,
        Guid stationId,
        UpdateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        if (!CanManageStation(restaurantId))
        {
            return Forbid();
        }

        KitchenStationResponse? item = await service.UpdateStationAsync(
            restaurantId,
            stationId,
            request,
            ct
        );
        return item is null
            ? NotFound()
            : Accepted($"/api/catalog/restaurants/{restaurantId}/stations", item);
    }

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/name")]
    public Task<ActionResult<KitchenStationResponse>> SetStationName(
        Guid restaurantId,
        Guid stationId,
        [FromBody] string name,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationNameAsync(restaurantId, stationId, name, ct)
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/priority")]
    public Task<ActionResult<KitchenStationResponse>> SetStationPriority(
        Guid restaurantId,
        Guid stationId,
        [FromBody] int priority,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationPriorityAsync(restaurantId, stationId, priority, ct)
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/active")]
    public Task<ActionResult<KitchenStationResponse>> SetStationActive(
        Guid restaurantId,
        Guid stationId,
        [FromBody] bool active,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationActiveAsync(restaurantId, stationId, active, ct)
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/primary")]
    public Task<ActionResult<KitchenStationResponse>> SetStationPrimary(
        Guid restaurantId,
        Guid stationId,
        [FromBody] bool primary,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationPrimaryAsync(restaurantId, stationId, primary, ct)
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/requires-primary-dispatch")]
    public Task<ActionResult<KitchenStationResponse>> SetStationRequiresPrimaryDispatch(
        Guid restaurantId,
        Guid stationId,
        [FromBody] bool requiresDispatch,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () =>
                service.SetStationRequiresPrimaryDispatchAsync(
                    restaurantId,
                    stationId,
                    requiresDispatch,
                    ct
                )
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/print-ticket")]
    public Task<ActionResult<KitchenStationResponse>> SetStationPrintTicket(
        Guid restaurantId,
        Guid stationId,
        [FromBody] bool printTicket,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationPrintTicketAsync(restaurantId, stationId, printTicket, ct)
        );

    [HttpPut("restaurants/{restaurantId:guid}/stations/{stationId:guid}/manages-zones")]
    public Task<ActionResult<KitchenStationResponse>> SetStationManagesZones(
        Guid restaurantId,
        Guid stationId,
        [FromBody] bool managesZones,
        CancellationToken ct
    ) =>
        SaveStationChangeAsync(
            restaurantId,
            () => service.SetStationManagesZonesAsync(restaurantId, stationId, managesZones, ct)
        );

    private async Task<ActionResult<KitchenStationResponse>> SaveStationChangeAsync(
        Guid restaurantId,
        Func<Task<KitchenStationResponse?>> change
    )
    {
        if (!CanManageStation(restaurantId))
        {
            return Forbid();
        }

        KitchenStationResponse? item = await change();
        return item is null
            ? NotFound()
            : Accepted($"/api/catalog/restaurants/{restaurantId}/stations", item);
    }

    private bool CanManageStation(Guid restaurantId) =>
        !User.IsInRole("Admin")
        && User.HasClaim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Manager")
        && User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage);
}
