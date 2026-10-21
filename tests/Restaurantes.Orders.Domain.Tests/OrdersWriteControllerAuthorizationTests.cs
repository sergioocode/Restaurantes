using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Restaurantes.Orders.Api.Controllers;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrdersWriteControllerAuthorizationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_requires_authentication_and_both_order_permissions()
    {
        Fixture fixture = new();
        CreateOrderRequest request = new() { RestaurantId = fixture.RestaurantId, Source = "Pos" };

        ActionResult<OrderResponse> anonymous = await fixture.Controller.Create(request, null, Ct);
        fixture.Authenticate(RestaurantPermissions.OrdersCreate);
        ActionResult<OrderResponse> missingClientPermission = await fixture.Controller.Create(
            request,
            null,
            Ct
        );
        fixture.Authenticate(RestaurantPermissions.OrdersCreate, RestaurantPermissions.PosUse);
        ActionResult<OrderResponse> unsupportedSource = await fixture.Controller.Create(
            new() { RestaurantId = fixture.RestaurantId, Source = "Unknown" },
            null,
            Ct
        );

        Assert.IsType<UnauthorizedResult>(anonymous.Result);
        Assert.IsType<ForbidResult>(missingClientPermission.Result);
        Assert.IsType<ForbidResult>(unsupportedSource.Result);
    }

    [Fact]
    public async Task Kds_transitions_require_existing_order_and_kds_permission()
    {
        Fixture fixture = new();
        fixture.Order = null;
        ActionResult<OrderResponse> missing = await fixture.Controller.StartPreparation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Ct
        );
        fixture.Order = CreateOrder(fixture.RestaurantId);
        ActionResult<OrderResponse> anonymous = await fixture.Controller.StartPreparation(
            fixture.Order.Id,
            Guid.NewGuid(),
            Ct
        );
        fixture.Authenticate(RestaurantPermissions.OrdersManage);
        ActionResult<OrderResponse> forbidden = await fixture.Controller.MarkReady(
            fixture.Order.Id,
            Guid.NewGuid(),
            Ct
        );
        ActionResult<OrderResponse> forbiddenDispatch = await fixture.Controller.DispatchStation(
            fixture.Order.Id,
            Guid.NewGuid(),
            Ct
        );

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.IsType<UnauthorizedResult>(anonymous.Result);
        Assert.IsType<ForbidResult>(forbidden.Result);
        Assert.IsType<ForbidResult>(forbiddenDispatch.Result);
    }

    [Fact]
    public async Task Management_transitions_require_authentication_and_order_management_scope()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);

        ActionResult<OrderResponse> anonymous = await fixture.Controller.Deliver(
            fixture.Order.Id,
            Ct
        );
        fixture.Authenticate(RestaurantPermissions.OrdersCreate);
        ActionResult<OrderResponse> forbidden = await fixture.Controller.Deliver(
            fixture.Order.Id,
            Ct
        );
        ActionResult<OrderResponse> forbiddenCancel = await fixture.Controller.Cancel(
            fixture.Order.Id,
            new CancelOrderRequest { Reason = "duplicate" },
            Ct
        );
        ActionResult<OrderResponse> forbiddenCancelLine = await fixture.Controller.CancelLine(
            fixture.Order.Id,
            Guid.NewGuid(),
            new CancelOrderLineRequest { Reason = "duplicate" },
            Ct
        );

        Assert.IsType<UnauthorizedResult>(anonymous.Result);
        Assert.IsType<ForbidResult>(forbidden.Result);
        Assert.IsType<ForbidResult>(forbiddenCancel.Result);
        Assert.IsType<ForbidResult>(forbiddenCancelLine.Result);
    }

    [Fact]
    public async Task Submit_requires_existing_order_authentication_and_source_permission()
    {
        Fixture fixture = new();
        fixture.Order = null;
        ActionResult<OrderResponse> missing = await fixture.Controller.Submit(
            Guid.NewGuid(),
            null,
            Ct
        );
        fixture.Order = CreateOrder(fixture.RestaurantId);
        ActionResult<OrderResponse> anonymous = await fixture.Controller.Submit(
            fixture.Order.Id,
            null,
            Ct
        );
        fixture.Authenticate(RestaurantPermissions.OrdersManage);
        ActionResult<OrderResponse> forbidden = await fixture.Controller.Submit(
            fixture.Order.Id,
            null,
            Ct
        );
        ActionResult<OrderResponse> forbiddenCancelLine = await fixture.Controller.CancelLine(
            fixture.Order.Id,
            Guid.NewGuid(),
            new CancelOrderLineRequest { Reason = "duplicate" },
            Ct
        );

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.IsType<UnauthorizedResult>(anonymous.Result);
        Assert.IsType<ForbidResult>(forbidden.Result);
        Assert.IsType<ForbidResult>(forbiddenCancelLine.Result);
    }

    private static Order CreateOrder(Guid restaurantId) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 1",
            string.Empty,
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Producto",
                    10m,
                    1,
                    string.Empty,
                    null,
                    "General",
                    "PASTA",
                    "Pasta",
                    false,
                    1
                ),
            ],
            DateTime.UtcNow
        );

    private sealed class Fixture
    {
        private readonly InterfaceProxy writeStoreProxy;

        public Fixture()
        {
            IOrderWriteStore store = Proxy<IOrderWriteStore>(out writeStoreProxy);
            IOrderCatalogStore catalog = Proxy<IOrderCatalogStore>(out _);
            IOrderPaymentStore payments = Proxy<IOrderPaymentStore>(out _);
            IDiningSessionStore dining = Proxy<IDiningSessionStore>(out _);
            IOrderCashRegister cashRegister = Proxy<IOrderCashRegister>(out _);
            OrderCommandService commands = new(
                store,
                catalog,
                payments,
                dining,
                TimeProvider.System
            );
            DbContextOptions<OrderWriteDbContext> options =
                new DbContextOptionsBuilder<OrderWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options;
            Controller = new OrdersWriteController(
                commands,
                store,
                dining,
                cashRegister,
                new OrderWriteDbContext(options),
                new ConfigurationBuilder().Build()
            );
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            };
            writeStoreProxy.Handler = (method, _) =>
                method.Name switch
                {
                    nameof(IOrderWriteStore.FindAsync) => Task.FromResult(Order),
                    _ => throw new NotSupportedException(method.Name),
                };
        }

        public OrdersWriteController Controller { get; }
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public Order? Order { get; set; }

        public void Authenticate(params string[] permissions)
        {
            Claim[] claims = new Claim[permissions.Length];
            for (int index = 0; index < permissions.Length; index++)
            {
                claims[index] = new Claim(
                    RestaurantClaimTypes.Permission,
                    RestaurantSecurityExtensions.ScopedPermission(RestaurantId, permissions[index])
                );
            }
            Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
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
