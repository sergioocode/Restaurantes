using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Orders.Api.Controllers;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrdersReadControllerTests
{
    private static readonly DateOnly BusinessDate = new(2026, 10, 21);

    [Fact]
    public async Task Kds_history_validates_scope_and_normalizes_station_and_zone()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Fixture fixture = new();

        ActionResult<IReadOnlyList<KitchenHistoryResponse>> invalid =
            await fixture.Controller.KitchenHistory(Guid.Empty, "DRINKS", Ct);
        fixture.SetUser(restaurantId, RestaurantPermissions.KdsUse);
        ActionResult<IReadOnlyList<KitchenHistoryResponse>> result =
            await fixture.Controller.KitchenHistory(restaurantId, " drinks ", Ct, zoneId);

        Assert.IsType<BadRequestResult>(invalid.Result);
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(restaurantId, fixture.HistoryRestaurantId);
        Assert.Equal(BusinessDate, fixture.HistoryDate);
        Assert.Equal("DRINKS", fixture.HistoryStation);
        Assert.Equal(zoneId, fixture.HistoryZoneId);
    }

    [Fact]
    public async Task Kds_history_requires_permission_and_forbids_missing_history_scope()
    {
        Guid restaurantId = Guid.NewGuid();
        Fixture fixture = new();

        ActionResult<IReadOnlyList<KitchenHistoryResponse>> denied =
            await fixture.Controller.KitchenHistory(restaurantId, "CHEF", Ct);
        fixture.SetUser(restaurantId, RestaurantPermissions.KdsUse);
        fixture.HistoryResult = null;
        ActionResult<IReadOnlyList<KitchenHistoryResponse>> forbidden =
            await fixture.Controller.KitchenHistory(restaurantId, "CHEF", Ct);

        Assert.IsType<ForbidResult>(denied.Result);
        Assert.IsType<ForbidResult>(forbidden.Result);
    }

    [Fact]
    public async Task Kds_list_rejects_zone_without_station_and_requires_kds_permission()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Fixture fixture = new();

        ActionResult<IReadOnlyList<OrderResponse>> invalid = await fixture.Controller.ListKds(
            restaurantId,
            Ct,
            zoneId: zoneId
        );
        ActionResult<IReadOnlyList<OrderResponse>> forbidden = await fixture.Controller.ListKds(
            restaurantId,
            Ct
        );
        fixture.SetUser(restaurantId, RestaurantPermissions.KdsUse);
        ActionResult<IReadOnlyList<OrderResponse>> result = await fixture.Controller.ListKds(
            restaurantId,
            Ct,
            "BEBIDAS",
            zoneId
        );

        Assert.IsType<BadRequestResult>(invalid.Result);
        Assert.IsType<ForbidResult>(forbidden.Result);
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(zoneId, fixture.ListZoneId);
        Assert.Equal("BEBIDAS", fixture.ListStation);
    }

    [Fact]
    public async Task Pos_list_returns_only_pos_and_customer_qr_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        Fixture fixture = new();
        fixture.Orders =
        [
            Order(restaurantId, "Pos"),
            Order(restaurantId, "CustomerQr"),
            Order(restaurantId, "Comandero"),
        ];
        fixture.SetUser(restaurantId, RestaurantPermissions.PosUse);

        ActionResult<IReadOnlyList<OrderResponse>> result = await fixture.Controller.ListPos(
            restaurantId,
            Ct
        );

        OrderResponse[] orders = Assert.IsType<OrderResponse[]>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
        Assert.Equal(["Pos", "CustomerQr"], orders.Select(order => order.Source));
    }

    [Fact]
    public async Task Commander_list_requires_permission_and_returns_only_commander_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        Fixture fixture = new();
        fixture.Orders = [Order(restaurantId, "Comandero"), Order(restaurantId, "Pos")];

        ActionResult<IReadOnlyList<OrderResponse>> denied = await fixture.Controller.ListCommander(
            restaurantId,
            Ct
        );
        fixture.SetUser(restaurantId, RestaurantPermissions.CommanderUse);
        ActionResult<IReadOnlyList<OrderResponse>> result = await fixture.Controller.ListCommander(
            restaurantId,
            Ct
        );

        Assert.IsType<ForbidResult>(denied.Result);
        OrderResponse[] orders = Assert.IsType<OrderResponse[]>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
        Assert.Equal(["Comandero"], orders.Select(order => order.Source));
    }

    [Fact]
    public async Task Management_and_cash_close_orders_enforce_scope_and_offsets()
    {
        Guid restaurantId = Guid.NewGuid();
        Fixture fixture = new();

        ActionResult<IReadOnlyList<ManagementOrderResponse>> invalidManagement =
            await fixture.Controller.ManagementOrders(restaurantId, BusinessDate, Ct, -1);
        ActionResult<IReadOnlyList<ManagementOrderResponse>> deniedManagement =
            await fixture.Controller.ManagementOrders(restaurantId, BusinessDate, Ct);
        fixture.SetUser(restaurantId, RestaurantPermissions.OrdersReportRead);
        ActionResult<IReadOnlyList<ManagementOrderResponse>> management =
            await fixture.Controller.ManagementOrders(restaurantId, BusinessDate, Ct, 10);
        ActionResult<IReadOnlyList<CashCloseOrderResponse>> deniedCashClose =
            await fixture.Controller.CashCloseOrders(restaurantId, Ct);
        fixture.SetUser(restaurantId, RestaurantPermissions.CashRegisterManage);
        ActionResult<IReadOnlyList<CashCloseOrderResponse>> invalidCashClose =
            await fixture.Controller.CashCloseOrders(restaurantId, Ct, -1);
        ActionResult<IReadOnlyList<CashCloseOrderResponse>> cashClose =
            await fixture.Controller.CashCloseOrders(restaurantId, Ct, 5);

        Assert.IsType<BadRequestResult>(invalidManagement.Result);
        Assert.IsType<ForbidResult>(deniedManagement.Result);
        Assert.IsType<OkObjectResult>(management.Result);
        Assert.IsType<ForbidResult>(deniedCashClose.Result);
        Assert.IsType<BadRequestObjectResult>(invalidCashClose.Result);
        Assert.IsType<OkObjectResult>(cashClose.Result);
        Assert.Equal(10, fixture.ManagementOffset);
        Assert.Equal(5, fixture.CashCloseOffset);
    }

    [Fact]
    public async Task Get_returns_not_found_or_requires_permission_for_existing_order()
    {
        Guid restaurantId = Guid.NewGuid();
        Fixture fixture = new();
        fixture.Order = Order(restaurantId, "Pos");

        ActionResult<OrderResponse> denied = await fixture.Controller.Get(
            fixture.Order.Id,
            null,
            Ct
        );
        fixture.SetUser(
            restaurantId,
            RestaurantPermissions.PosUse,
            RestaurantPermissions.OrdersManage
        );
        ActionResult<OrderResponse> found = await fixture.Controller.Get(
            fixture.Order.Id,
            null,
            Ct
        );
        ActionResult<OrderResponse> missing = await fixture.Controller.Get(
            Guid.NewGuid(),
            null,
            Ct
        );

        Assert.IsType<ForbidResult>(denied.Result);
        Assert.Equal(fixture.Order, Assert.IsType<OkObjectResult>(found.Result).Value);
        Assert.IsType<NotFoundResult>(missing.Result);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OrderResponse Order(Guid restaurantId, string source) =>
        new(
            Guid.NewGuid(),
            restaurantId,
            null,
            null,
            null,
            "Mesa 1",
            "",
            "DineIn",
            source,
            "AtCheckout",
            "Submitted",
            10m,
            1,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            [],
            []
        );

    private sealed class Fixture
    {
        private readonly InterfaceProxy storeProxy;
        private readonly InterfaceProxy historyProxy;
        private readonly InterfaceProxy managementProxy;
        private readonly InterfaceProxy cashCloseProxy;

        public Fixture()
        {
            IKitchenOrderReadStore store = Proxy<IKitchenOrderReadStore>(out storeProxy);
            IOrderSettlementQuery settlements = Proxy<IOrderSettlementQuery>(out cashCloseProxy);
            IManagementOrderQuery management = Proxy<IManagementOrderQuery>(out managementProxy);
            IKitchenHistoryQuery history = Proxy<IKitchenHistoryQuery>(out historyProxy);
            IOrderCashRegister cashRegister = Proxy<IOrderCashRegister>(
                out InterfaceProxy cashProxy
            );
            IDiningSessionStore dining = Proxy<IDiningSessionStore>(out _);
            cashProxy.Handler = (method, _) =>
                method.Name switch
                {
                    nameof(IOrderCashRegister.GetBusinessDateAsync) => Task.FromResult(
                        BusinessDate
                    ),
                    nameof(IOrderCashRegister.EnsureOpenAsync) => Task.FromResult(
                        new OrderCashRegisterSession(Guid.NewGuid(), BusinessDate)
                    ),
                    _ => throw new NotSupportedException(method.Name),
                };
            Controller = new OrdersReadController(
                store,
                settlements,
                management,
                history,
                cashRegister,
                dining,
                new ConfigurationBuilder().Build()
            );
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            };
            storeProxy.Handler = (method, args) =>
                method.Name switch
                {
                    nameof(IKitchenOrderReadStore.FindAsync) => Task.FromResult(
                        Orders.FirstOrDefault(order => order.Id == (Guid)args[0]!)
                    ),
                    nameof(IKitchenOrderReadStore.ListAsync) => ListOrders(args),
                    _ => throw new NotSupportedException(method.Name),
                };
            historyProxy.Handler = (method, args) =>
            {
                HistoryRestaurantId = (Guid)args[0]!;
                HistoryDate = (DateOnly)args[1]!;
                HistoryStation = (string)args[2]!;
                HistoryZoneId = (Guid?)args[4];
                return Task.FromResult(HistoryResult);
            };
            managementProxy.Handler = (method, args) =>
            {
                ManagementOffset = (int)args[2]!;
                return Task.FromResult<IReadOnlyList<ManagementOrderResponse>>([]);
            };
            cashCloseProxy.Handler = (method, args) =>
            {
                CashCloseOffset = (int)args[1]!;
                return Task.FromResult<IReadOnlyList<CashCloseOrderResponse>>([]);
            };
        }

        public OrdersReadController Controller { get; }
        public IReadOnlyList<OrderResponse> Orders { get; set; } = [];
        public IReadOnlyList<KitchenHistoryResponse>? HistoryResult { get; set; } = [];
        public OrderResponse? Order
        {
            get => Orders.FirstOrDefault();
            set => Orders = value is null ? [] : [value];
        }

        public Guid HistoryRestaurantId { get; private set; }
        public DateOnly HistoryDate { get; private set; }
        public string? HistoryStation { get; private set; }
        public Guid? HistoryZoneId { get; private set; }
        public Guid? ListZoneId { get; private set; }
        public string? ListStation { get; private set; }
        public int ManagementOffset { get; private set; }
        public int CashCloseOffset { get; private set; }

        public void SetUser(Guid restaurantId, params string[] permissions)
        {
            Claim[] claims = permissions
                .Select(permission => new Claim(
                    RestaurantClaimTypes.Permission,
                    RestaurantSecurityExtensions.ScopedPermission(restaurantId, permission)
                ))
                .ToArray();
            Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        private Task<IReadOnlyList<OrderResponse>> ListOrders(object?[] args)
        {
            ListStation = args[1] as string;
            ListZoneId = args.Length > 3 ? (Guid?)args[3] : null;
            return Task.FromResult(Orders);
        }

        private static T Proxy<T>(out InterfaceProxy proxy)
            where T : class
        {
            T instance = DispatchProxy.Create<T, InterfaceProxy>();
            proxy = (InterfaceProxy)(object)instance;
            proxy.Handler = (_, _) => throw new NotSupportedException();
            return instance;
        }
    }

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args ?? []);
    }
}
