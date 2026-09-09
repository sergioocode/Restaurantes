using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Write.Controllers;

[ApiController, Authorize, Route("api/catalog")]
public sealed class CatalogController(CatalogCommandService service) : ControllerBase
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

        CategoryResponse? item = await service.UpdateCategoryAsync(id, request, ct);
        return item is null ? NotFound() : Accepted($"/api/catalog/categories/{id}", item);
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

        ProductResponse? item = await service.UpdateProductAsync(id, request, ct);
        return item is null ? NotFound() : Accepted($"/api/catalog/products/{id}", item);
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
        return item is null
            ? NotFound(
                new ProblemDetails
                {
                    Title = "Product or category was not found or is inactive",
                    Status = 404,
                }
            )
            : Accepted($"/api/catalog/restaurants/{restaurantId}/menu", item);
    }

    [HttpPost("restaurants/{restaurantId:guid}/stations/defaults")]
    public async Task<ActionResult<IReadOnlyList<KitchenStationResponse>>> EnsureDefaultStations(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage)
            ? (ActionResult<IReadOnlyList<KitchenStationResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<KitchenStationResponse>>)
                Accepted(await service.EnsureDefaultStationsAsync(restaurantId, ct));
    }

    [HttpPost("restaurants/{restaurantId:guid}/stations")]
    public async Task<ActionResult<KitchenStationResponse>> CreateStation(
        Guid restaurantId,
        CreateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        if (!User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage))
        {
            return Forbid();
        }

        KitchenStationResponse? item = await service.CreateStationAsync(restaurantId, request, ct);
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
        if (!User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage))
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
}
