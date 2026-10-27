using Microsoft.AspNetCore.Mvc;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Controllers;

[ApiController, Route("api/catalog")]
public sealed class CatalogReadController(ICatalogReadStore store) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> Categories(
        [FromQuery] Guid? restaurantId,
        CancellationToken ct
    )
    {
        if (
            restaurantId is Guid id
            && !User.CanAccessRestaurant(id, RestaurantPermissions.CatalogManage)
        )
        {
            return Forbid();
        }

        return Ok(
            restaurantId is Guid restaurant
                ? await store.ListCategoriesAsync(restaurant, ct)
                : await store.ListCategoriesAsync(ct)
        );
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
        CancellationToken ct,
        CatalogClientChannel channel = CatalogClientChannel.Pos
    )
    {
        if (!Enum.IsDefined(channel))
        {
            return BadRequest();
        }
        return Ok(await store.ListMenuAsync(restaurantId, channel, ct));
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
        return
            !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse)
            && !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage)
            ? (ActionResult<IReadOnlyList<KitchenStationResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<KitchenStationResponse>>)
                Ok(await store.ListStationsAsync(restaurantId, ct));
    }
}
