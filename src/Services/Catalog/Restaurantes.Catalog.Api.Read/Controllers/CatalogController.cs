using Microsoft.AspNetCore.Mvc;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Read.Controllers;

[ApiController, Route("api/catalog")]
public sealed class CatalogController(ICatalogReadStore store) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> Categories(
        CancellationToken ct
    )
    {
        return Ok(await store.ListCategoriesAsync(ct));
    }

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> Products(CancellationToken ct)
    {
        return Ok(await store.ListProductsAsync(ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/menu")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IReadOnlyList<MenuItemResponse>>> Menu(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return Ok(await store.ListMenuAsync(restaurantId, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/menu/configuration")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IReadOnlyList<MenuItemResponse>>> MenuConfiguration(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage)
            ? (ActionResult<IReadOnlyList<MenuItemResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<MenuItemResponse>>)
                Ok(await store.ListMenuConfigurationAsync(restaurantId, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/stations")]
    public async Task<ActionResult<IReadOnlyList<KitchenStationResponse>>> Stations(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse)
            ? (ActionResult<IReadOnlyList<KitchenStationResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<KitchenStationResponse>>)
                Ok(await store.ListStationsAsync(restaurantId, ct));
    }
}
