using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Api.Controllers;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.RestaurantOperations.Domain;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantsControllerCoverageTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Read_endpoints_list_get_and_return_not_found_for_a_missing_restaurant()
    {
        RestaurantResponse restaurant = Response(Guid.NewGuid(), "Salón");
        ReadStore store = new([restaurant]);
        RestaurantsReadController controller = new(store);

        ActionResult<IReadOnlyList<RestaurantResponse>> listed = await controller.List(
            CancellationToken.None
        );
        ActionResult<RestaurantResponse> found = await controller.Get(
            restaurant.Id,
            CancellationToken.None
        );
        ActionResult<RestaurantResponse> missing = await controller.Get(
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.Same(store.Restaurants, Assert.IsType<OkObjectResult>(listed.Result).Value);
        Assert.Same(restaurant, Assert.IsType<OkObjectResult>(found.Result).Value);
        Assert.IsType<NotFoundResult>(missing.Result);
    }

    [Fact]
    public async Task Accessible_lists_filter_inactive_and_unpermitted_restaurants_for_each_client()
    {
        Guid allowedId = Guid.NewGuid();
        Guid inactiveId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        RestaurantResponse allowed = Response(allowedId, "Zulu");
        RestaurantResponse earlierAllowed = Response(Guid.NewGuid(), "Alfa");
        RestaurantResponse inactive = Response(inactiveId, "Inactivo") with { IsActive = false };
        RestaurantResponse unpermitted = Response(otherId, "Sin permiso");
        ReadStore store = new([allowed, inactive, unpermitted, earlierAllowed]);
        RestaurantsReadController controller = new(store)
        {
            ControllerContext = Context(
                Permission(allowedId, RestaurantPermissions.KdsUse),
                Permission(allowedId, RestaurantPermissions.CommanderUse),
                Permission(allowedId, RestaurantPermissions.PosUse),
                Permission(inactiveId, RestaurantPermissions.KdsUse),
                Permission(inactiveId, RestaurantPermissions.CommanderUse),
                Permission(inactiveId, RestaurantPermissions.PosUse),
                Permission(earlierAllowed.Id, RestaurantPermissions.KdsUse),
                Permission(earlierAllowed.Id, RestaurantPermissions.CommanderUse),
                Permission(earlierAllowed.Id, RestaurantPermissions.PosUse)
            ),
        };

        ActionResult<IReadOnlyList<RestaurantResponse>> kds = await controller.ListKdsAccessible(
            CancellationToken.None
        );
        ActionResult<IReadOnlyList<RestaurantResponse>> commander =
            await controller.ListCommanderAccessible(CancellationToken.None);
        ActionResult<IReadOnlyList<RestaurantResponse>> pos = await controller.ListPosAccessible(
            CancellationToken.None
        );

        foreach (
            ActionResult<IReadOnlyList<RestaurantResponse>> result in new[] { kds, commander, pos }
        )
        {
            RestaurantResponse[] rows = Assert.IsType<RestaurantResponse[]>(
                Assert.IsType<OkObjectResult>(result.Result).Value
            );
            Assert.Equal([earlierAllowed.Id, allowedId], rows.Select(row => row.Id));
        }
    }

    [Fact]
    public async Task Create_forbids_non_admin_and_maps_duplicate_to_conflict_or_success_to_accepted()
    {
        CreateRestaurantRequest request = CreateRequest();
        Guid companyId = request.CompanyId!.Value;
        WriteStore store = new(companyId) { CodeExists = true };
        RestaurantCommandService service = new(store, TimeProvider.System);
        RestaurantsWriteController forbiddenController = new(service)
        {
            ControllerContext = Context(),
        };
        RestaurantsWriteController adminController = new(service)
        {
            ControllerContext = Context(Role("Admin")),
        };

        Assert.IsType<ForbidResult>(
            (await forbiddenController.Create(request, CancellationToken.None)).Result
        );
        Assert.IsType<ConflictObjectResult>(
            (await adminController.Create(request, CancellationToken.None)).Result
        );
        Assert.Equal(0, store.SaveCount);

        store.CodeExists = false;
        ActionResult<RestaurantResponse> created = await adminController.Create(
            request,
            CancellationToken.None
        );
        AcceptedResult accepted = Assert.IsType<AcceptedResult>(created.Result);
        RestaurantResponse response = Assert.IsType<RestaurantResponse>(accepted.Value);
        Assert.Equal($"/api/restaurant-operations/restaurants/{response.Id}", accepted.Location);
        Assert.Equal("LOCAL-1", response.Code);
        Assert.Equal(response.Id, store.LastEvent?.RestaurantId);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Update_requires_table_permission_and_maps_missing_or_updated_restaurants()
    {
        Restaurant restaurant = Restaurant.Create(
            "R-1",
            "Anterior",
            "Calle 1",
            Guid.NewGuid(),
            Now
        );
        WriteStore store = new(restaurant.CompanyId!.Value, restaurant);
        RestaurantsWriteController forbidden = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(),
        };
        RestaurantsWriteController allowed = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(
                Permission(restaurant.Id, RestaurantPermissions.TablesManage)
            ),
        };
        UpdateRestaurantRequest request = new() { Name = "Actualizado", Address = "Calle 2" };

        Assert.IsType<ForbidResult>(
            (await forbidden.Update(restaurant.Id, request, CancellationToken.None)).Result
        );
        Guid missingId = Guid.NewGuid();
        Assert.IsType<ForbidResult>(
            (await allowed.Update(missingId, request, CancellationToken.None)).Result
        );
        RestaurantsWriteController authorizedMissing = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(Permission(missingId, RestaurantPermissions.TablesManage)),
        };
        Assert.IsType<NotFoundResult>(
            (await authorizedMissing.Update(missingId, request, CancellationToken.None)).Result
        );

        ActionResult<RestaurantResponse> updated = await allowed.Update(
            restaurant.Id,
            request,
            CancellationToken.None
        );
        AcceptedResult accepted = Assert.IsType<AcceptedResult>(updated.Result);
        RestaurantResponse response = Assert.IsType<RestaurantResponse>(accepted.Value);
        Assert.Equal("Actualizado", response.Name);
        Assert.Equal(2, response.Version);
        Assert.Equal($"/api/restaurant-operations/restaurants/{restaurant.Id}", accepted.Location);
    }

    [Fact]
    public async Task Set_company_is_admin_only_and_reports_missing_restaurant_or_company()
    {
        Restaurant restaurant = Restaurant.Create(
            "R-1",
            "Restaurante",
            "Calle 1",
            Guid.NewGuid(),
            Now
        );
        Guid targetCompany = Guid.NewGuid();
        WriteStore store = new(restaurant.CompanyId!.Value, restaurant);
        RestaurantsWriteController forbidden = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(
                Permission(restaurant.Id, RestaurantPermissions.TablesManage)
            ),
        };
        RestaurantsWriteController admin = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(Role("Admin")),
        };
        SetRestaurantCompanyRequest request = new() { CompanyId = targetCompany };

        Assert.IsType<ForbidResult>(
            (await forbidden.SetCompany(restaurant.Id, request, CancellationToken.None)).Result
        );
        Assert.IsType<NotFoundResult>(
            (await admin.SetCompany(Guid.NewGuid(), request, CancellationToken.None)).Result
        );
        Assert.IsType<NotFoundResult>(
            (await admin.SetCompany(restaurant.Id, request, CancellationToken.None)).Result
        );

        store.AvailableCompanyId = targetCompany;
        ActionResult<RestaurantResponse> changed = await admin.SetCompany(
            restaurant.Id,
            request,
            CancellationToken.None
        );
        AcceptedResult accepted = Assert.IsType<AcceptedResult>(changed.Result);
        Assert.Equal(targetCompany, Assert.IsType<RestaurantResponse>(accepted.Value).CompanyId);
        Assert.Equal(2, restaurant.Version);
    }

    [Fact]
    public async Task Set_active_requires_table_permission_and_returns_not_found_or_updated_state()
    {
        Restaurant restaurant = Restaurant.Create(
            "R-1",
            "Restaurante",
            "Calle 1",
            Guid.NewGuid(),
            Now
        );
        WriteStore store = new(restaurant.CompanyId!.Value, restaurant);
        RestaurantsWriteController forbidden = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(),
        };
        RestaurantsWriteController allowed = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(
                Permission(restaurant.Id, RestaurantPermissions.TablesManage)
            ),
        };
        SetRestaurantActiveRequest request = new() { IsActive = false };

        Assert.IsType<ForbidResult>(
            (await forbidden.SetActive(restaurant.Id, request, CancellationToken.None)).Result
        );
        Guid missingId = Guid.NewGuid();
        Assert.IsType<ForbidResult>(
            (await allowed.SetActive(missingId, request, CancellationToken.None)).Result
        );
        RestaurantsWriteController authorizedMissing = new(new(store, TimeProvider.System))
        {
            ControllerContext = Context(Permission(missingId, RestaurantPermissions.TablesManage)),
        };
        Assert.IsType<NotFoundResult>(
            (await authorizedMissing.SetActive(missingId, request, CancellationToken.None)).Result
        );

        ActionResult<RestaurantResponse> changed = await allowed.SetActive(
            restaurant.Id,
            request,
            CancellationToken.None
        );
        AcceptedResult accepted = Assert.IsType<AcceptedResult>(changed.Result);
        Assert.False(Assert.IsType<RestaurantResponse>(accepted.Value).IsActive);
        Assert.Equal(2, restaurant.Version);
    }

    private static RestaurantResponse Response(Guid id, string name) =>
        new(id, $"R-{name}", name, "Calle 1", null, true, 1, Now);

    private static CreateRestaurantRequest CreateRequest() =>
        new()
        {
            Code = " local-1 ",
            Name = "Restaurante",
            Address = "Calle 1",
            CompanyId = Guid.NewGuid(),
        };

    private static ControllerContext Context(params Claim[] claims) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            },
        };

    private static Claim Permission(Guid restaurantId, string permission) =>
        new(RestaurantClaimTypes.Permission, $"{restaurantId:N}:{permission}");

    private static Claim Role(string role) => new(ClaimTypes.Role, role);

    private sealed class ReadStore(IReadOnlyList<RestaurantResponse> restaurants)
        : IRestaurantReadStore
    {
        public IReadOnlyList<RestaurantResponse> Restaurants { get; } = restaurants;

        public Task<IReadOnlyList<RestaurantResponse>> ListAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult(Restaurants);

        public Task<RestaurantResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Restaurants.SingleOrDefault(restaurant => restaurant.Id == id));
    }

    private sealed class WriteStore(Guid availableCompanyId, Restaurant? restaurant = null)
        : IRestaurantWriteStore
    {
        public Guid AvailableCompanyId { get; set; } = availableCompanyId;
        public bool CodeExists { get; set; }
        public int SaveCount { get; private set; }
        public RestaurantChanged? LastEvent { get; private set; }
        private Restaurant? CurrentRestaurant { get; set; } = restaurant;

        public Task<bool> CodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken
        ) => Task.FromResult(CodeExists);

        public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(companyId == AvailableCompanyId);

        public Task<Restaurant?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(CurrentRestaurant?.Id == id ? CurrentRestaurant : null);

        public Task SaveWithEventAsync(
            Restaurant restaurant,
            RestaurantChanged integrationEvent,
            CancellationToken cancellationToken
        )
        {
            CurrentRestaurant = restaurant;
            LastEvent = integrationEvent;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
