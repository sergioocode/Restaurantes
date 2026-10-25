using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task Customer_qr_create_and_submit_reject_unconfigured_client_networks()
    {
        Fixture fixture = new();
        fixture.Controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.15");

        ActionResult<OrderResponse> create = await fixture.Controller.Create(
            new CreateOrderRequest
            {
                RestaurantId = fixture.RestaurantId,
                Source = nameof(OrderSource.CustomerQr),
            },
            "guest-token",
            Ct
        );
        fixture.Order = CreateOrder(fixture.RestaurantId, OrderSource.CustomerQr);
        ActionResult<OrderResponse> submit = await fixture.Controller.Submit(
            fixture.Order.Id,
            "guest-token",
            Ct
        );

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(create.Result).StatusCode
        );
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(submit.Result).StatusCode
        );
        Assert.Empty(fixture.SavedEvents);
    }

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
    public async Task Create_pos_persists_order_and_event_after_cash_register_and_catalog_checks()
    {
        Fixture fixture = new();
        Guid productId = Guid.NewGuid();
        fixture.Authenticate(RestaurantPermissions.OrdersCreate, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> unavailable = await fixture.Controller.Create(
            CreateRequest(fixture.RestaurantId, productId),
            null,
            Ct
        );

        Assert.IsType<ConflictObjectResult>(unavailable.Result);
        Assert.Empty(fixture.SavedEvents);

        fixture.CatalogProducts[productId] = CatalogProduct(productId, isAvailable: true);
        fixture.Authenticate(RestaurantPermissions.OrdersCreate, RestaurantPermissions.PosUse);
        ActionResult<OrderResponse> result = await fixture.Controller.Create(
            CreateRequest(fixture.RestaurantId, productId),
            null,
            Ct
        );

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
        OrderResponse response = Assert.IsType<OrderResponse>(accepted.Value);
        Restaurantes.Orders.Contracts.Events.OrderCreated created =
            Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderCreated>(
                Assert.Single(fixture.SavedEvents)
            );
        Assert.Equal(response.Id, created.OrderId);
        Assert.Equal(fixture.RestaurantId, created.RestaurantId);
        Assert.Equal("Pos", created.Source);
        Assert.Null(created.CreatedByUserId);
        Assert.Equal("Draft", response.Status);
    }

    [Fact]
    public async Task Create_maps_invalid_domain_order_to_bad_request()
    {
        Fixture fixture = new();
        Guid productId = Guid.NewGuid();
        fixture.CatalogProducts[productId] = CatalogProduct(productId, isAvailable: true);
        fixture.Authenticate(RestaurantPermissions.OrdersCreate, RestaurantPermissions.PosUse);
        CreateOrderRequest request = CreateRequest(
            fixture.RestaurantId,
            productId,
            tableLabel: new string('M', 81)
        );

        ActionResult<OrderResponse> result = await fixture.Controller.Create(request, null, Ct);

        ProblemDetails problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(result.Result).Value
        );
        Assert.Equal("Invalid order", problem.Title);
        Assert.Empty(fixture.SavedEvents);
    }

    [Fact]
    public async Task Create_reports_a_closed_cash_register_as_conflict()
    {
        Fixture fixture = new();
        Guid productId = Guid.NewGuid();
        fixture.CatalogProducts[productId] = CatalogProduct(productId, isAvailable: true);
        fixture.CashRegisterError = new OrderCashRegisterClosedException(fixture.RestaurantId);
        fixture.Authenticate(RestaurantPermissions.OrdersCreate, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.Create(
            CreateRequest(fixture.RestaurantId, productId),
            null,
            Ct
        );

        ProblemDetails problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<ConflictObjectResult>(result.Result).Value
        );
        Assert.Equal("Caja cerrada", problem.Title);
        Assert.Empty(fixture.SavedEvents);
    }

    [Fact]
    public async Task Create_comandero_requires_a_user_identifier_even_when_permissions_are_valid()
    {
        Fixture fixture = new();
        fixture.Authenticate(
            RestaurantPermissions.OrdersCreate,
            RestaurantPermissions.CommanderUse
        );

        ActionResult<OrderResponse> result = await fixture.Controller.Create(
            CreateRequest(fixture.RestaurantId, Guid.NewGuid(), source: "Comandero"),
            null,
            Ct
        );

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(fixture.SavedEvents);
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

    [Fact]
    public async Task Submit_returns_conflict_when_the_cash_register_is_closed()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        fixture.CashRegisterError = new OrderCashRegisterClosedException(fixture.RestaurantId);
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.Submit(
            fixture.Order.Id,
            null,
            Ct
        );

        ProblemDetails problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<ConflictObjectResult>(result.Result).Value
        );
        Assert.Equal("Order cannot be submitted", problem.Title);
        Assert.Empty(fixture.SavedEvents);
    }

    [Fact]
    public async Task Submitted_order_can_be_prepared_readied_and_delivered_through_authorized_routes()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> submit = await fixture.Controller.Submit(
            fixture.Order.Id,
            null,
            Ct
        );
        AcceptedResult submitted = Assert.IsType<AcceptedResult>(submit.Result);
        Assert.Equal("Submitted", Assert.IsType<OrderResponse>(submitted.Value).Status);

        Guid lineId = fixture.Order.Lines.Single().Id;
        fixture.Authenticate(RestaurantPermissions.KdsUse);
        ActionResult<OrderResponse> start = await fixture.Controller.StartPreparation(
            fixture.Order.Id,
            lineId,
            Ct
        );
        AcceptedResult preparing = Assert.IsType<AcceptedResult>(start.Result);
        Assert.Equal("InPreparation", Assert.IsType<OrderResponse>(preparing.Value).Status);

        ActionResult<OrderResponse> markReady = await fixture.Controller.MarkReady(
            fixture.Order.Id,
            lineId,
            Ct
        );
        AcceptedResult ready = Assert.IsType<AcceptedResult>(markReady.Result);
        Assert.Equal("Ready", Assert.IsType<OrderResponse>(ready.Value).Status);

        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);
        ActionResult<OrderResponse> deliver = await fixture.Controller.Deliver(
            fixture.Order.Id,
            Ct
        );
        AcceptedResult delivered = Assert.IsType<AcceptedResult>(deliver.Result);
        Assert.Equal("Delivered", Assert.IsType<OrderResponse>(delivered.Value).Status);

        Assert.Collection(
            fixture.SavedEvents,
            item => Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderSubmitted>(item),
            item =>
                Assert.IsType<Restaurantes.Orders.Contracts.Events.KitchenTicketPreparationStarted>(
                    item
                ),
            item => Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderReady>(item),
            item => Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderDelivered>(item)
        );
    }

    [Fact]
    public async Task Deliver_returns_conflict_when_order_is_not_ready()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.Deliver(fixture.Order.Id, Ct);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Order cannot be delivered", problem.Title);
    }

    [Fact]
    public async Task Cancel_cancels_unpaid_order_with_an_open_cash_register()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.Cancel(
            fixture.Order.Id,
            new CancelOrderRequest { Reason = "duplicate" },
            Ct
        );

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
        Assert.Equal("Cancelled", Assert.IsType<OrderResponse>(accepted.Value).Status);
        Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderCancelled>(
            Assert.Single(fixture.SavedEvents)
        );
    }

    [Fact]
    public async Task Cancellation_reports_concurrent_write_conflict_without_publishing_event()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        fixture.WriteError = new DbUpdateConcurrencyException("The order version changed.");
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.Cancel(
            fixture.Order.Id,
            new CancelOrderRequest { Reason = "duplicate" },
            Ct
        );

        ProblemDetails problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<ConflictObjectResult>(result.Result).Value
        );
        Assert.Equal("Order changed concurrently", problem.Title);
        Assert.Empty(fixture.SavedEvents);
    }

    [Fact]
    public async Task CancelLine_cancels_active_unpaid_line_with_an_open_cash_register()
    {
        Fixture fixture = new();
        fixture.Order = CreateOrder(fixture.RestaurantId);
        Guid lineId = fixture.Order.Lines.Single().Id;
        fixture.Authenticate(RestaurantPermissions.OrdersManage, RestaurantPermissions.PosUse);

        ActionResult<OrderResponse> result = await fixture.Controller.CancelLine(
            fixture.Order.Id,
            lineId,
            new CancelOrderLineRequest { Reason = "duplicate" },
            Ct
        );

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
        OrderResponse response = Assert.IsType<OrderResponse>(accepted.Value);
        Assert.Equal("Cancelled", response.Status);
        Assert.Equal("Cancelled", Assert.Single(response.Lines).Status);
        Assert.IsType<Restaurantes.Orders.Contracts.Events.OrderLineCancelled>(
            Assert.Single(fixture.SavedEvents)
        );
    }

    private static Order CreateOrder(Guid restaurantId, OrderSource source = OrderSource.Pos) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 1",
            string.Empty,
            ServiceMode.DineIn,
            source,
            source == OrderSource.CustomerQr ? PaymentTiming.Immediate : PaymentTiming.OnAccount,
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

    private static CreateOrderRequest CreateRequest(
        Guid restaurantId,
        Guid productId,
        string source = "Pos",
        string tableLabel = "Pedido para llevar"
    ) =>
        new()
        {
            RestaurantId = restaurantId,
            DiningSessionId = Guid.NewGuid(),
            TableLabel = tableLabel,
            CustomerName = "Cliente de prueba",
            Source = source,
            ServiceMode = "Takeaway",
            PaymentTiming = "OnAccount",
            Lines = [new CreateOrderLineRequest { ProductId = productId, Quantity = 2 }],
        };

    private static OrderCatalogProduct CatalogProduct(Guid productId, bool isAvailable) =>
        new(
            Guid.Empty,
            productId,
            "Producto de prueba",
            5m,
            Guid.NewGuid(),
            "Platos",
            "PASTAS",
            "Pastas",
            false,
            1,
            isAvailable,
            [],
            "PLATO",
            [new OrderCatalogFormat("PLATO", 5m)]
        );

    private sealed class Fixture
    {
        private readonly InterfaceProxy writeStoreProxy;
        private readonly InterfaceProxy catalogProxy;
        private readonly InterfaceProxy cashRegisterProxy;
        private readonly InterfaceProxy diningProxy;

        public Fixture()
        {
            IOrderWriteStore store = Proxy<IOrderWriteStore>(out writeStoreProxy);
            IOrderCatalogStore catalog = Proxy<IOrderCatalogStore>(out catalogProxy);
            IOrderPaymentStore payments = Proxy<IOrderPaymentStore>(
                out InterfaceProxy paymentProxy
            );
            IDiningSessionStore dining = Proxy<IDiningSessionStore>(out diningProxy);
            diningProxy.Handler = (method, _) =>
                method.Name == nameof(IDiningSessionStore.EnsureOpenAsync)
                    ? Task.FromResult(new DiningSessionValidation(null, null))
                    : throw new NotSupportedException(method.Name);
            IOrderCashRegister cashRegister = Proxy<IOrderCashRegister>(out cashRegisterProxy);
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
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
                    )
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
            writeStoreProxy.Handler = (method, arguments) =>
                method.Name switch
                {
                    nameof(IOrderWriteStore.FindAsync) => Task.FromResult(Order),
                    nameof(IOrderWriteStore.SaveWithEventAsync) => SaveEvent(arguments),
                    _ => throw new NotSupportedException(method.Name),
                };
            cashRegisterProxy.Handler = (method, _) =>
                method.Name switch
                {
                    nameof(IOrderCashRegister.EnsureOpenAsync) => CashRegisterError is null
                        ? Task.FromResult(
                            new OrderCashRegisterSession(
                                Guid.NewGuid(),
                                DateOnly.FromDateTime(DateTime.UtcNow)
                            )
                        )
                        : throw CashRegisterError,
                    _ => throw new NotSupportedException(method.Name),
                };
            paymentProxy.Handler = (method, _) =>
                method.Name switch
                {
                    nameof(IOrderPaymentStore.IsPaidAsync) => Task.FromResult(false),
                    nameof(IOrderPaymentStore.IsRefundedAsync) => Task.FromResult(false),
                    _ => throw new NotSupportedException(method.Name),
                };
            catalogProxy.Handler = (method, arguments) =>
                method.Name switch
                {
                    nameof(IOrderCatalogStore.FindAsync) => Task.FromResult<
                        IReadOnlyDictionary<Guid, OrderCatalogProduct>
                    >(
                        CatalogProducts
                            .Where(product =>
                                ((IReadOnlyCollection<Guid>)arguments[1]!).Contains(product.Key)
                            )
                            .ToDictionary()
                    ),
                    _ => throw new NotSupportedException(method.Name),
                };
        }

        public OrdersWriteController Controller { get; }
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public Order? Order { get; set; }
        public Exception? CashRegisterError { get; set; }
        public Exception? WriteError { get; set; }
        public List<object> SavedEvents { get; } = [];
        public Dictionary<Guid, OrderCatalogProduct> CatalogProducts { get; } = [];

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

        public void AuthenticateAsUser(Guid userId, params string[] permissions)
        {
            Claim[] permissionClaims = permissions
                .Select(permission => new Claim(
                    RestaurantClaimTypes.Permission,
                    RestaurantSecurityExtensions.ScopedPermission(RestaurantId, permission)
                ))
                .ToArray();
            Controller.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [.. permissionClaims, new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "test"
                )
            );
        }

        private static T Proxy<T>(out InterfaceProxy proxy)
            where T : class
        {
            T instance = DispatchProxy.Create<T, InterfaceProxy>();
            proxy = (InterfaceProxy)(object)instance;
            proxy.Handler = (_, _) => throw new NotSupportedException();
            return instance;
        }

        private Task SaveEvent(object?[] arguments)
        {
            if (WriteError is not null)
            {
                throw WriteError;
            }

            SavedEvents.Add((object)arguments[1]!);
            return Task.CompletedTask;
        }
    }

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args ?? []);
    }
}
