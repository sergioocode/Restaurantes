using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Reporting.Api.Controllers;
using Restaurantes.Reporting.Api.DeadLetters;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingReadControllerCoverageTests
{
    [Fact]
    public async Task Dashboard_forbids_a_requested_restaurant_without_access()
    {
        FakeReadStore store = new();
        ReportingReadController controller = CreateController(store, User());

        ActionResult<DailyDashboard> result = await controller.Dashboard(
            null,
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Null(store.RestaurantIds);
    }

    [Fact]
    public async Task Dashboard_uses_the_requested_restaurant_when_scoped_role_allows_it()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeReadStore store = new();
        ReportingReadController controller = CreateController(
            store,
            User(new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Gerente"))
        );

        ActionResult<DailyDashboard> result = await controller.Dashboard(
            new DateOnly(2026, 10, 22),
            restaurantId,
            CancellationToken.None,
            20
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal([restaurantId], store.RestaurantIds);
        Assert.Equal(new DateOnly(2026, 10, 22), store.BusinessDate);
        Assert.Equal(20, store.OrderLimit);
    }

    [Fact]
    public async Task Dashboard_uses_global_scope_for_a_global_reader()
    {
        FakeReadStore store = new();
        ReportingReadController controller = CreateController(
            store,
            User(new Claim(ClaimTypes.Role, "Gerente"))
        );

        ActionResult<DailyDashboard> result = await controller.Dashboard(
            null,
            null,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Null(store.RestaurantIds);
    }

    [Fact]
    public async Task Dashboard_filters_claims_to_distinct_authorized_restaurants()
    {
        Guid roleRestaurantId = Guid.NewGuid();
        Guid permissionRestaurantId = Guid.NewGuid();
        FakeReadStore store = new();
        ReportingReadController controller = CreateController(
            store,
            User(
                new Claim(RestaurantClaimTypes.RestaurantRole, $"{roleRestaurantId:N}:Gerente"),
                new Claim(
                    RestaurantClaimTypes.Permission,
                    RestaurantSecurityExtensions.ScopedPermission(
                        permissionRestaurantId,
                        RestaurantPermissions.DashboardRead
                    )
                ),
                new Claim(RestaurantClaimTypes.RestaurantId, roleRestaurantId.ToString()),
                new Claim(RestaurantClaimTypes.RestaurantId, roleRestaurantId.ToString("N")),
                new Claim(RestaurantClaimTypes.RestaurantId, permissionRestaurantId.ToString()),
                new Claim(RestaurantClaimTypes.RestaurantId, Guid.NewGuid().ToString()),
                new Claim(RestaurantClaimTypes.RestaurantId, "not-a-guid"),
                new Claim(RestaurantClaimTypes.RestaurantId, Guid.Empty.ToString())
            )
        );

        ActionResult<DailyDashboard> result = await controller.Dashboard(
            null,
            null,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(
            new[] { roleRestaurantId, permissionRestaurantId }.Order(),
            store.RestaurantIds!.Order()
        );
    }

    [Fact]
    public async Task Dashboard_and_health_forbid_users_without_dashboard_permission()
    {
        ReportingReadController controller = CreateController(new FakeReadStore(), User());

        Assert.IsType<ForbidResult>(
            (await controller.Dashboard(null, null, CancellationToken.None)).Result
        );
        Assert.IsType<ForbidResult>(await controller.Health(CancellationToken.None));
    }

    [Fact]
    public async Task Health_returns_probe_results_for_a_scoped_reader()
    {
        MonitoredEndpoint endpoint = new("orders", "http://orders/health");
        RecordingProbe probe = new();
        ReportingReadController controller = CreateController(
            new FakeReadStore(),
            User(new Claim(RestaurantClaimTypes.RestaurantRole, $"{Guid.NewGuid():N}:Gerente")),
            new StaticCatalog([endpoint]),
            probe
        );

        ActionResult result = await controller.Health(CancellationToken.None);

        OkObjectResult response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new[] { endpoint }, probe.Checked);
        Assert.Equal(
            new[] { new EndpointHealth(endpoint.Service, endpoint.Url, true, 200) },
            Assert.IsType<IReadOnlyList<EndpointHealth>>(response.Value, exactMatch: false)
        );
    }

    private static ReportingReadController CreateController(
        FakeReadStore store,
        ClaimsPrincipal user,
        IMonitoredEndpointCatalog? catalog = null,
        IEndpointHealthProbe? probe = null
    )
    {
        ReportingReadController controller = new(
            new DashboardQueryService(store, TimeProvider.System, TimeZoneInfo.Utc),
            new ReportingHealthService(
                catalog ?? new StaticCatalog([]),
                probe ?? new RecordingProbe()
            ),
            null!
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user },
        };
        return controller;
    }

    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));

    private sealed class FakeReadStore : IReportingReadStore
    {
        public DateOnly BusinessDate { get; private set; }
        public IReadOnlyCollection<Guid>? RestaurantIds { get; private set; }
        public int OrderLimit { get; private set; }

        public Task<DailyReportingSnapshot> GetDailySnapshotAsync(
            DateOnly businessDate,
            DateTime startUtc,
            DateTime endUtc,
            IReadOnlyCollection<Guid>? restaurantIds,
            int orderLimit,
            CancellationToken ct
        )
        {
            BusinessDate = businessDate;
            RestaurantIds = restaurantIds;
            OrderLimit = orderLimit;
            return Task.FromResult(new DailyReportingSnapshot([], new HashSet<Guid>(), []));
        }
    }

    private sealed class StaticCatalog(IReadOnlyCollection<MonitoredEndpoint> endpoints)
        : IMonitoredEndpointCatalog
    {
        public IReadOnlyCollection<MonitoredEndpoint> GetAll() => endpoints;
    }

    private sealed class RecordingProbe : IEndpointHealthProbe
    {
        public List<MonitoredEndpoint> Checked { get; } = [];

        public Task<EndpointHealth> CheckAsync(MonitoredEndpoint endpoint, CancellationToken ct)
        {
            Checked.Add(endpoint);
            return Task.FromResult(new EndpointHealth(endpoint.Service, endpoint.Url, true, 200));
        }
    }
}
