using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderCommandServiceCreateTests
{
    [Fact]
    public async Task Cancel_returns_null_without_checking_payment_when_order_is_missing()
    {
        RecordingStores stores = new();
        OrderCommandService service = CreateService(stores);

        OrderResponse? response = await service.CancelAsync(
            Guid.NewGuid(),
            "Customer request",
            "manager",
            TestContext.Current.CancellationToken
        );

        Assert.Null(response);
        Assert.Equal(0, stores.PaymentChecks);
        Assert.Null(stores.IntegrationEvent);
    }

    [Fact]
    public async Task Cancel_persists_a_trimmed_event_and_returns_the_cancelled_order()
    {
        (OrderCommandService service, RecordingStores stores, OrderResponse order) =
            await CreateCancelableOrderAsync();

        OrderResponse? response = await service.CancelAsync(
            order.Id,
            "  Customer request  ",
            "  manager  ",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(response);
        Assert.Equal("Cancelled", response.Status);
        Assert.Equal(
            "Customer request",
            Assert.IsType<OrderCancelled>(stores.IntegrationEvent).CancellationReason
        );
        Assert.Equal("manager", Assert.IsType<OrderCancelled>(stores.IntegrationEvent).CancelledBy);
        Assert.Equal(
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            Assert.IsType<OrderCancelled>(stores.IntegrationEvent).CancelledAtUtc
        );
    }

    [Fact]
    public async Task Cancel_line_persists_the_changed_line_and_returns_updated_order()
    {
        (OrderCommandService service, RecordingStores stores, OrderResponse order) =
            await CreateCancelableOrderAsync();
        Guid lineId = Assert.Single(order.Lines).Id;

        OrderResponse? response = await service.CancelLineAsync(
            order.Id,
            lineId,
            "  Sin guarnición  ",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(response);
        Assert.Equal("Cancelled", response.Status);
        OrderLineCancelled integrationEvent = Assert.IsType<OrderLineCancelled>(
            stores.IntegrationEvent
        );
        Assert.Equal(lineId, integrationEvent.ChangedLineId);
        Assert.Equal("Sin guarnición", integrationEvent.CancellationReason);
        Assert.Equal("Cancelled", integrationEvent.Status);
        Assert.Equal(
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            integrationEvent.CancelledAtUtc
        );
    }

    [Fact]
    public async Task Paid_orders_cannot_be_cancelled_through_order_or_line_commands()
    {
        (OrderCommandService service, RecordingStores stores, OrderResponse order) =
            await CreateCancelableOrderAsync();
        stores.Paid = true;
        object? existingEvent = stores.IntegrationEvent;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelAsync(
                order.Id,
                "Customer request",
                "manager",
                TestContext.Current.CancellationToken
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelLineAsync(
                order.Id,
                Assert.Single(order.Lines).Id,
                "Customer request",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(2, stores.PaymentChecks);
        Assert.Same(existingEvent, stores.IntegrationEvent);
    }

    [Fact]
    public async Task Create_parses_enums_before_calling_stores()
    {
        RecordingStores stores = new();
        OrderCommandService service = CreateService(stores);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(source: "pos"),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(stores.CatalogCalled);
        Assert.Null(stores.SavedOrder);
    }

    [Fact]
    public async Task Create_requires_open_dining_location_for_dine_in_and_bar_orders()
    {
        RecordingStores stores = new();
        OrderCommandService service = CreateService(stores);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(serviceMode: "DineIn", tableId: null, diningSessionId: Guid.NewGuid()),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(serviceMode: "DineIn", tableId: Guid.NewGuid(), diningSessionId: null),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(stores.DiningCalled);
    }

    [Fact]
    public async Task Create_pos_uses_selected_format_extras_and_expected_price()
    {
        Guid productId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        OrderCatalogProduct product = Product(
            productId,
            isAvailable: false,
            extraGroups:
            [
                new OrderExtraGroup(
                    groupId,
                    "Extras",
                    1,
                    2,
                    [new OrderExtraOption(extraId, "Queso", 2m, 10m)]
                ),
            ]
        );
        RecordingStores stores = new([product]);
        OrderCommandService service = CreateService(stores);
        CreateOrderRequest request = Request(
            lines:
            [
                new CreateOrderLineRequest
                {
                    ProductId = productId,
                    Format = " racion ",
                    ExpectedUnitPrice = 7m,
                    Quantity = 2,
                    Notes = "  Sin cebolla  ",
                    SelectedExtras =
                    [
                        new CreateOrderExtraRequest
                        {
                            GroupId = groupId,
                            ProductId = extraId,
                            Quantity = 1,
                        },
                    ],
                },
            ]
        );

        Guid createdBy = Guid.NewGuid();
        OrderResponse response = await service.CreateAsync(
            request,
            "token",
            CashSession,
            TestContext.Current.CancellationToken,
            createdByUserId: createdBy
        );

        Assert.True(stores.CatalogCalled);
        Assert.False(stores.DiningCalled);
        Assert.Equal("RACION", Assert.Single(response.Lines).Format);
        Assert.Equal(7m, Assert.Single(response.Lines).UnitPrice);
        Assert.Equal(5m, Assert.Single(response.Lines).BaseUnitPrice);
        Assert.Equal("Sin cebolla · Extras: Queso × 1", Assert.Single(response.Lines).Notes);
        Assert.Equal(14m, response.Total);
        OrderCreated created = Assert.IsType<OrderCreated>(stores.IntegrationEvent);
        Assert.Equal(response.Id, created.OrderId);
        Assert.Equal(createdBy, created.CreatedByUserId);
    }

    [Fact]
    public async Task Create_non_pos_rejects_unavailable_product_and_unavailable_format()
    {
        Guid productId = Guid.NewGuid();
        RecordingStores stores = new([Product(productId, isAvailable: false)]);
        OrderCommandService service = CreateService(stores);

        InvalidOperationException unavailable = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    Request(
                        source: "CustomerQr",
                        serviceMode: "Takeaway",
                        paymentTiming: "Immediate"
                    ),
                    "token",
                    CashSession,
                    TestContext.Current.CancellationToken
                )
        );
        Assert.Contains("unavailable", unavailable.Message, StringComparison.OrdinalIgnoreCase);

        stores.Products[productId] = Product(productId, isAvailable: true);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(
                    source: "CustomerQr",
                    serviceMode: "Takeaway",
                    paymentTiming: "Immediate",
                    lines: [new CreateOrderLineRequest { ProductId = productId, Format = "TAPA" }]
                ),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Theory]
    [InlineData(0, "La selección de extras")]
    [InlineData(3, "La selección de extras")]
    public async Task Create_rejects_extra_quantities_outside_group_limits(
        int quantity,
        string message
    )
    {
        Guid productId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        RecordingStores stores = new([
            Product(
                productId,
                isAvailable: true,
                extraGroups:
                [
                    new OrderExtraGroup(
                        groupId,
                        "Extras",
                        1,
                        2,
                        [new OrderExtraOption(extraId, "Queso", 2m)]
                    ),
                ]
            ),
        ]);
        OrderCommandService service = CreateService(stores);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(
                    lines:
                    [
                        new CreateOrderLineRequest
                        {
                            ProductId = productId,
                            SelectedExtras =
                            [
                                new CreateOrderExtraRequest
                                {
                                    GroupId = groupId,
                                    ProductId = extraId,
                                    Quantity = quantity,
                                },
                            ],
                        },
                    ]
                ),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Create_rejects_zero_quantity_when_group_does_not_require_an_extra()
    {
        Guid productId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        RecordingStores stores = new([
            Product(
                productId,
                extraGroups:
                [
                    new OrderExtraGroup(
                        groupId,
                        "Extras",
                        0,
                        2,
                        [new OrderExtraOption(extraId, "Queso", 2m)]
                    ),
                ]
            ),
        ]);
        OrderCommandService service = CreateService(stores);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(
                    lines:
                    [
                        new CreateOrderLineRequest
                        {
                            ProductId = productId,
                            SelectedExtras =
                            [
                                new CreateOrderExtraRequest
                                {
                                    GroupId = groupId,
                                    ProductId = extraId,
                                    Quantity = 0,
                                },
                            ],
                        },
                    ]
                ),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("no está disponible", error.Message);
    }

    [Fact]
    public async Task Create_rejects_unknown_extra_group_and_overlong_composed_notes()
    {
        Guid productId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid unknownGroupId = Guid.NewGuid();
        OrderCatalogProduct product = Product(
            productId,
            extraGroups:
            [
                new OrderExtraGroup(
                    groupId,
                    "Extras",
                    0,
                    1,
                    [new OrderExtraOption(extraId, "Queso", 1m)]
                ),
            ]
        );
        RecordingStores stores = new([product]);
        OrderCommandService service = CreateService(stores);

        ArgumentException unknown = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(
                    lines:
                    [
                        new CreateOrderLineRequest
                        {
                            ProductId = productId,
                            SelectedExtras =
                            [
                                new CreateOrderExtraRequest
                                {
                                    GroupId = unknownGroupId,
                                    ProductId = extraId,
                                },
                            ],
                        },
                    ]
                ),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("no son válidos", unknown.Message);

        ArgumentException notes = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(
                Request(
                    lines:
                    [
                        new CreateOrderLineRequest
                        {
                            ProductId = productId,
                            Notes = new string('x', 500),
                            SelectedExtras =
                            [
                                new CreateOrderExtraRequest
                                {
                                    GroupId = groupId,
                                    ProductId = extraId,
                                    Quantity = 1,
                                },
                            ],
                        },
                    ]
                ),
                "token",
                CashSession,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("superan el límite", notes.Message);
    }

    [Fact]
    public async Task Create_dine_in_captures_the_table_zone_in_order_and_event()
    {
        Guid productId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        RecordingStores stores = new([Product(productId)]);
        OrderCommandService service = CreateService(stores);

        OrderResponse response = await service.CreateAsync(
            Request(
                serviceMode: "DineIn",
                tableId: tableId,
                diningSessionId: Guid.NewGuid(),
                lines: [new CreateOrderLineRequest { ProductId = productId, Quantity = 1 }]
            ),
            "token",
            CashSession,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(tableId, response.ZoneId);
        Assert.Equal(tableId, stores.SavedOrder?.ZoneId);
        Assert.Equal(tableId, Assert.IsType<OrderCreated>(stores.IntegrationEvent).ZoneId);
    }

    private static OrderCommandService CreateService(RecordingStores stores) =>
        new(stores, stores, stores, stores, new FixedTimeProvider());

    private static async Task<(
        OrderCommandService Service,
        RecordingStores Stores,
        OrderResponse Order
    )> CreateCancelableOrderAsync()
    {
        Guid productId = Guid.NewGuid();
        RecordingStores stores = new([Product(productId)]);
        OrderCommandService service = CreateService(stores);
        OrderResponse order = await service.CreateAsync(
            Request(lines: [new CreateOrderLineRequest { ProductId = productId, Quantity = 1 }]),
            "token",
            CashSession,
            TestContext.Current.CancellationToken
        );
        return (service, stores, order);
    }

    private static CreateOrderRequest Request(
        string source = "Pos",
        string serviceMode = "Takeaway",
        string paymentTiming = "OnAccount",
        Guid? tableId = null,
        Guid? diningSessionId = null,
        IReadOnlyList<CreateOrderLineRequest>? lines = null
    ) =>
        new()
        {
            RestaurantId = Guid.NewGuid(),
            TableId = tableId,
            DiningSessionId = diningSessionId,
            TableLabel = "Mesa 1",
            CustomerName = serviceMode == "Takeaway" ? "Ana" : "",
            ServiceMode = serviceMode,
            Source = source,
            PaymentTiming = paymentTiming,
            Lines = lines?.ToList() ?? [new CreateOrderLineRequest { ProductId = Guid.NewGuid() }],
        };

    private static OrderCatalogProduct Product(
        Guid productId,
        bool isAvailable = true,
        IReadOnlyList<OrderExtraGroup>? extraGroups = null
    ) =>
        new(
            Guid.NewGuid(),
            productId,
            "Pasta",
            10m,
            Guid.NewGuid(),
            "Principales",
            "COCINA",
            "Cocina",
            false,
            1,
            isAvailable,
            extraGroups ?? [],
            "PLATO",
            [new OrderCatalogFormat("PLATO", 10m), new OrderCatalogFormat("RACION", 6m)],
            RequiresKds: true,
            TaxRate: 10m
        );

    private static readonly OrderCashRegisterSession CashSession = new(
        Guid.NewGuid(),
        new DateOnly(2026, 10, 20)
    );

    private sealed class RecordingStores(IReadOnlyCollection<OrderCatalogProduct>? products = null)
        : IOrderWriteStore,
            IOrderCatalogStore,
            IOrderPaymentStore,
            IDiningSessionStore
    {
        public Dictionary<Guid, OrderCatalogProduct> Products { get; } =
            (products ?? []).ToDictionary(product => product.ProductId);
        public bool CatalogCalled { get; private set; }
        public bool DiningCalled { get; private set; }
        public bool Paid { get; set; }
        public int PaymentChecks { get; private set; }
        public Restaurantes.Orders.Domain.Order? SavedOrder { get; private set; }
        public object? IntegrationEvent { get; private set; }

        public Task<Order?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(SavedOrder?.Id == id ? SavedOrder : null);

        public Task SaveWithEventAsync(Order order, object integrationEvent, CancellationToken ct)
        {
            SavedOrder = order;
            IntegrationEvent = integrationEvent;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, OrderCatalogProduct>> FindAsync(
            Guid restaurantId,
            IReadOnlyCollection<Guid> productIds,
            CancellationToken ct
        )
        {
            CatalogCalled = true;
            return Task.FromResult<IReadOnlyDictionary<Guid, OrderCatalogProduct>>(
                Products.Where(pair => productIds.Contains(pair.Key)).ToDictionary()
            );
        }

        public Task<bool> IsPaidAsync(Guid orderId, CancellationToken ct)
        {
            PaymentChecks++;
            return Task.FromResult(Paid);
        }

        public Task<bool> IsRefundedAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<DiningSessionValidation> EnsureOpenAsync(
            Guid diningSessionId,
            Guid restaurantId,
            Guid tableId,
            string source,
            string serviceMode,
            string customerAccessToken,
            CancellationToken ct
        )
        {
            DiningCalled = true;
            return Task.FromResult(new DiningSessionValidation(2, tableId));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
