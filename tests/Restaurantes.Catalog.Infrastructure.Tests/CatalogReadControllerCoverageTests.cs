using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Catalog.Api.Controllers;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogReadControllerCoverageTests
{
    [Fact]
    public async Task Categories_products_and_menu_return_store_results_and_forward_channel()
    {
        Guid restaurantId = Guid.NewGuid();
        CatalogReadController controller = CreateController(out ReadStoreProxy store);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.IsType<OkObjectResult>((await controller.Categories(ct)).Result);
        Assert.IsType<OkObjectResult>((await controller.Products(ct)).Result);
        Assert.IsType<OkObjectResult>(
            (await controller.Menu(restaurantId, ct, CatalogClientChannel.CustomerQr)).Result
        );
        Assert.Equal(restaurantId, store.RestaurantId);
        Assert.Equal(CatalogClientChannel.CustomerQr, store.Channel);

        BadRequestResult invalidChannel = Assert.IsType<BadRequestResult>(
            (await controller.Menu(restaurantId, ct, (CatalogClientChannel)100)).Result
        );
        Assert.NotNull(invalidChannel);
        Assert.Equal(1, store.Calls.Count(call => call == nameof(ICatalogReadStore.ListMenuAsync)));
    }

    [Fact]
    public async Task Menu_configuration_requires_scoped_catalog_management_permission()
    {
        Guid restaurantId = Guid.NewGuid();
        CatalogReadController denied = CreateController(out ReadStoreProxy deniedStore);

        Assert.IsType<ForbidResult>(
            (
                await denied.MenuConfiguration(restaurantId, TestContext.Current.CancellationToken)
            ).Result
        );
        Assert.Empty(deniedStore.Calls);

        CatalogReadController allowed = CreateController(
            out ReadStoreProxy allowedStore,
            RestaurantPermissions.CatalogManage,
            restaurantId
        );

        Assert.IsType<OkObjectResult>(
            (
                await allowed.MenuConfiguration(restaurantId, TestContext.Current.CancellationToken)
            ).Result
        );
        Assert.Contains(nameof(ICatalogReadStore.ListMenuConfigurationAsync), allowedStore.Calls);
    }

    [Fact]
    public async Task Station_reads_accept_kds_or_catalog_management_permission()
    {
        Guid restaurantId = Guid.NewGuid();
        CatalogReadController denied = CreateController(out ReadStoreProxy deniedStore);
        Assert.IsType<ForbidResult>(
            (await denied.Stations(restaurantId, TestContext.Current.CancellationToken)).Result
        );
        Assert.Empty(deniedStore.Calls);

        CatalogReadController kds = CreateController(
            out ReadStoreProxy kdsStore,
            RestaurantPermissions.KdsUse,
            restaurantId
        );
        Assert.IsType<OkObjectResult>(
            (await kds.Stations(restaurantId, TestContext.Current.CancellationToken)).Result
        );
        Assert.Contains(nameof(ICatalogReadStore.ListStationsAsync), kdsStore.Calls);

        CatalogReadController catalogManager = CreateController(
            out ReadStoreProxy catalogStore,
            RestaurantPermissions.CatalogManage,
            restaurantId
        );
        Assert.IsType<OkObjectResult>(
            (
                await catalogManager.Stations(restaurantId, TestContext.Current.CancellationToken)
            ).Result
        );
        Assert.Contains(nameof(ICatalogReadStore.ListStationsAsync), catalogStore.Calls);
    }

    private static CatalogReadController CreateController(
        out ReadStoreProxy proxy,
        string? permission = null,
        Guid restaurantId = default
    )
    {
        ICatalogReadStore store = DispatchProxy.Create<ICatalogReadStore, ReadStoreProxy>();
        proxy = (ReadStoreProxy)(object)store;
        ClaimsPrincipal principal = permission is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(
                            RestaurantClaimTypes.Permission,
                            RestaurantSecurityExtensions.ScopedPermission(restaurantId, permission)
                        ),
                    ],
                    "test"
                )
            );
        return new CatalogReadController(store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    public class ReadStoreProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public Guid? RestaurantId { get; private set; }
        public CatalogClientChannel? Channel { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string method = targetMethod?.Name ?? throw new NotSupportedException();
            Calls.Add(method);
            if (method == nameof(ICatalogReadStore.ListMenuAsync))
            {
                RestaurantId = (Guid)args![0]!;
                Channel = (CatalogClientChannel)args[1]!;
            }

            return method switch
            {
                nameof(ICatalogReadStore.ListCategoriesAsync) => Task.FromResult<
                    IReadOnlyList<CategoryResponse>
                >([]),
                nameof(ICatalogReadStore.ListProductsAsync) => Task.FromResult<
                    IReadOnlyList<ProductResponse>
                >([]),
                nameof(ICatalogReadStore.ListMenuAsync) => Task.FromResult<
                    IReadOnlyList<MenuItemResponse>
                >([]),
                nameof(ICatalogReadStore.ListMenuConfigurationAsync) => Task.FromResult<
                    IReadOnlyList<MenuItemResponse>
                >([]),
                nameof(ICatalogReadStore.ListStationsAsync) => Task.FromResult<
                    IReadOnlyList<KitchenStationResponse>
                >([]),
                _ => throw new NotSupportedException(method),
            };
        }
    }
}
