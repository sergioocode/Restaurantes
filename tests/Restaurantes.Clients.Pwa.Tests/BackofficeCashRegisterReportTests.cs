using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeCashRegisterReportTests : BunitContext
{
    [Fact]
    public async Task Report_paging_requests_following_pages_until_a_short_page_is_returned()
    {
        List<int> requestedOffsets = [];
        Func<int, Task<List<int>>> loadPage = offset =>
        {
            requestedOffsets.Add(offset);
            return Task.FromResult(offset == 0 ? Enumerable.Range(0, 100).ToList() : [100]);
        };
        MethodInfo loadPages = typeof(BackofficeHome)
            .GetMethod("LoadPagesAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(int));

        Task<List<int>> task = (Task<List<int>>)loadPages.Invoke(null, [loadPage])!;
        List<int> results = await task;

        Assert.Equal([0, 100], requestedOffsets);
        Assert.Equal(101, results.Count);
        Assert.Equal(100, results[^1]);
    }

    [Fact]
    public void Cash_register_report_is_limited_to_restaurants_in_the_users_access()
    {
        Guid allowedId = Guid.NewGuid();
        Guid forbiddenId = Guid.NewGuid();
        BackofficeHome host = new();
        SetField(
            host,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = allowedId,
                    Name = "Local permitido",
                    IsActive = true,
                },
                new()
                {
                    Id = forbiddenId,
                    Name = "Local ajeno",
                    IsActive = true,
                },
            }
        );
        SetField(
            host,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "contabilidad", "Contabilidad"),
                [new RestaurantAccess(allowedId, "Contabilidad", ["cash-register.read"])]
            )
        );

        List<RestaurantResponse> result =
            (List<RestaurantResponse>)
                typeof(BackofficeHome)
                    .GetMethod(
                        "CashRegisterReportRestaurants",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!
                    .Invoke(host, null)!;

        Assert.Equal([allowedId], result.Select(restaurant => restaurant.Id));
    }

    [Fact]
    public void Global_cash_register_permission_allows_selecting_all_restaurants()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        BackofficeHome host = new();
        SetField(
            host,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = firstId,
                    Name = "Local uno",
                    IsActive = true,
                },
                new()
                {
                    Id = secondId,
                    Name = "Local dos",
                    IsActive = true,
                },
            }
        );
        SetField(
            host,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "gerencia", "Gerencia"),
                [],
                AllRestaurantsPermissions: ["cash-register.manage"]
            )
        );

        List<RestaurantResponse> result =
            (List<RestaurantResponse>)
                typeof(BackofficeHome)
                    .GetMethod(
                        "CashRegisterReportRestaurants",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!
                    .Invoke(host, null)!;

        Assert.Equal([firstId, secondId], result.Select(restaurant => restaurant.Id));
    }

    [Fact]
    public async Task Refreshing_cash_register_report_loads_only_the_selected_authorized_restaurant()
    {
        Guid allowedId = Guid.NewGuid();
        Guid forbiddenId = Guid.NewGuid();
        RecordingBackofficeHandler handler = new();
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(
            new Restaurantes.Clients.Backoffice.Pwa.Security.SessionAuthenticationStateProvider()
        );
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "contabilidad", "Contabilidad"),
                [new RestaurantAccess(allowedId, "Contabilidad", ["cash-register.read"])]
            )
        );
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = allowedId,
                    Name = "Local permitido",
                    IsActive = true,
                },
                new()
                {
                    Id = forbiddenId,
                    Name = "Local ajeno",
                    IsActive = true,
                },
            }
        );
        SetField(cut.Instance, "restaurantId", allowedId);
        SetField(cut.Instance, "cashRegisterReportDate", new DateOnly(2026, 10, 19));

        await cut.InvokeAsync(async () =>
        {
            MethodInfo refresh = typeof(BackofficeHome).GetMethod(
                "RefreshCashRegisterReport",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)refresh.Invoke(cut.Instance, null)!;
        });

        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains(
            handler.Requests,
            request => request.Contains($"/api/cash-register/restaurants/{allowedId}/history")
        );
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Contains(forbiddenId.ToString())
        );
        Assert.Contains(
            handler.Requests,
            request => request.Contains($"/api/sales?restaurantId={allowedId}")
        );
        Assert.Contains(
            handler.Requests,
            request => request.Contains($"/api/orders/management?restaurantId={allowedId}")
        );
    }

    [Fact]
    public async Task Backoffice_can_reopen_and_exit_the_saved_cash_register_ticket()
    {
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(new RecordingBackofficeHandler())
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton(
            new Restaurantes.Clients.Backoffice.Pwa.Security.SessionAuthenticationStateProvider()
        );
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        CashRegisterReportResponse closed = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 19),
            "Closed",
            55m,
            25m,
            25m,
            5m,
            3m,
            82m,
            82m,
            82m,
            82m,
            -1m,
            DateTime.UtcNow.AddHours(-1),
            "TPV Centro",
            DateTime.UtcNow,
            "TPV Centro",
            [
                new CashRegisterPaymentMethodTotal("Cash", 25m),
                new CashRegisterPaymentMethodTotal("Card", 15m),
            ],
            [
                new CashRegisterPaymentMethodExpected("Cash", 82m),
                new CashRegisterPaymentMethodExpected("Card", 15m),
            ],
            [
                new CashRegisterPaymentMethodReconciliation("Cash", 82m, 82m, 0m),
                new CashRegisterPaymentMethodReconciliation("Card", 15m, 14m, -1m),
            ],
            [
                new(
                    Guid.NewGuid(),
                    "Income",
                    "Cash",
                    40m,
                    "Factura #42",
                    DateTime.UtcNow.AddMinutes(-2),
                    "Aportación de caja",
                    "Cajero",
                    null
                ),
                new(
                    Guid.NewGuid(),
                    "Expense",
                    "Cash",
                    3m,
                    "Material",
                    DateTime.UtcNow.AddMinutes(-1),
                    "Pago a proveedor",
                    "Cajero",
                    null
                ),
                new(
                    Guid.NewGuid(),
                    "Sale",
                    "Card",
                    15m,
                    string.Empty,
                    DateTime.UtcNow,
                    string.Empty,
                    "Cajero",
                    null
                ),
            ]
        );

        await cut.InvokeAsync(async () =>
        {
            MethodInfo show = typeof(BackofficeHome).GetMethod(
                "ShowCashRegisterCloseTicket",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)show.Invoke(cut.Instance, [closed])!;
        });

        Assert.Contains("cash-register-close-ticket-preview", cut.Markup);
        Assert.Contains("Arqueo cerrado", cut.Markup);
        Assert.Contains("55,00 €", cut.Markup);
        Assert.Contains("Factura #42", cut.Markup);
        Assert.DoesNotContain("Aportación de caja", cut.Markup);
        Assert.Contains("Pago a proveedor · Material", cut.Markup);
        Assert.Contains("Tarjeta / datáfono", cut.Markup);
        Assert.Contains("cash-register-negative", cut.Markup);
        await cut.Find("button[aria-label='Salir del ticket de cierre']").ClickAsync();
        Assert.DoesNotContain("cash-register-close-ticket-preview", cut.Markup);
    }

    [Fact]
    public async Task Sale_ticket_renders_company_snapshot_and_order_fallback_details()
    {
        Guid restaurantId = Guid.NewGuid();
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(new RecordingBackofficeHandler())
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton(
            new Restaurantes.Clients.Backoffice.Pwa.Security.SessionAuthenticationStateProvider()
        );
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = restaurantId,
                    Name = "Local Centro",
                    Address = "Calle Mayor 1",
                    IsActive = true,
                    Company = new RestaurantCompanyResponse
                    {
                        LegalName = "Restaurantes SL",
                        TaxId = "B12345678",
                        FiscalAddress = "Calle Fiscal 2",
                        Phone = "910000000",
                        Email = "caja@example.test",
                    },
                },
            }
        );
        SaleResponse frozenSale = new(
            Guid.NewGuid(),
            restaurantId,
            "Pos",
            "Mixed",
            7m,
            "Completed",
            1,
            DateTime.UtcNow,
            [],
            [
                new SaleLineSnapshot(
                    Guid.NewGuid(),
                    "Café",
                    3.5m,
                    2,
                    "Bebidas",
                    "BAR",
                    "Grande",
                    3m,
                    [new SaleLineExtraSnapshot(Guid.NewGuid(), "Leche", 0.5m, 1)]
                ),
            ],
            1m,
            [new SalePaymentBreakdown("Card", 1, 8m)],
            "Promoción",
            10m,
            0.5m
        );

        await ShowSaleTicket(cut, frozenSale);

        Assert.Contains("Restaurantes SL", cut.Markup);
        Assert.Contains("B12345678", cut.Markup);
        Assert.Contains("910000000", cut.Markup);
        Assert.Contains("caja@example.test", cut.Markup);
        Assert.Contains("Café", cut.Markup);
        Assert.Contains("Leche", cut.Markup);
        Assert.Contains("Descuento (10 %)", cut.Markup);
        Assert.Contains("Medio de pago · Tarjeta / datáfono", cut.Markup);
        Assert.Contains("Propina", cut.Markup);

        Guid orderId = Guid.NewGuid();
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = restaurantId,
                    Name = "Local Centro",
                    Address = "Calle Mayor 1",
                    IsActive = true,
                },
            }
        );
        SetField(
            cut.Instance,
            "historyOrders",
            new List<ManagementOrderResponse>
            {
                new(
                    orderId,
                    restaurantId,
                    "Mesa 4",
                    "Cliente",
                    "Pos",
                    "Completed",
                    12m,
                    DateTime.UtcNow,
                    DateTimeOffset.Now,
                    "Cash",
                    null,
                    [
                        new ManagementOrderLineResponse(
                            "Tostada",
                            2,
                            6m,
                            "Delivered",
                            "Media",
                            5m,
                            [new ManagementOrderExtraResponse(Guid.NewGuid(), "Tomate", 1m, 1)]
                        ),
                        new ManagementOrderLineResponse("Cancelado", 1, 9m, "Cancelled"),
                    ],
                    3
                ),
            }
        );
        SaleResponse legacySale = frozenSale with
        {
            Id = Guid.NewGuid(),
            PaymentMethod = "Mixed",
            OrderIds = [orderId],
            Lines = [],
            PaymentMethods = null,
            DiscountAmount = 0m,
            TipTotal = 0m,
        };

        await ShowSaleTicket(cut, legacySale);

        Assert.Contains("Calle Mayor 1", cut.Markup);
        Assert.Contains("Mesa 4", cut.Markup);
        Assert.Contains("Comensales: 3", cut.Markup);
        Assert.Contains("Tostada", cut.Markup);
        Assert.Contains("Tomate", cut.Markup);
        Assert.DoesNotContain("Cancelado", cut.Markup);
        Assert.Contains("Sin desglose disponible", cut.Markup);
        Assert.DoesNotContain("Propina", cut.Markup);
        await cut.Find("button[aria-label='Cerrar ticket de venta']").ClickAsync();
        Assert.DoesNotContain("sales-receipt", cut.Markup);
    }

    [Fact]
    public void Cash_movement_sale_lookup_uses_sale_id_then_falls_back_to_order_id()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SaleResponse matchingSale = new(
            Guid.NewGuid(),
            restaurantId,
            "Pos",
            "Cash",
            42m,
            "Completed",
            1,
            DateTime.UtcNow,
            [orderId],
            []
        );
        SaleResponse otherRestaurantSale = matchingSale with
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
        };
        BackofficeHome host = new();
        SetField(
            host,
            "dailyCashRegisterSales",
            new List<SaleResponse> { otherRestaurantSale, matchingSale }
        );
        CashRegisterReportResponse register = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Closed",
            0m,
            42m,
            42m,
            0m,
            0m,
            42m,
            42m,
            42m,
            42m,
            0m,
            DateTime.UtcNow,
            "TPV Centro",
            DateTime.UtcNow,
            "TPV Centro",
            [],
            [],
            [],
            []
        );
        MethodInfo lookup = typeof(BackofficeHome).GetMethod(
            "SaleForCashMovement",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        CashMovementReportResponse bySale = new(
            Guid.NewGuid(),
            "Income",
            "Cash",
            42m,
            "Pago",
            DateTime.UtcNow,
            "Venta",
            "TPV Centro",
            orderId,
            SaleId: matchingSale.Id
        );
        CashMovementReportResponse byOrder = bySale with { SaleId = null };
        CashMovementReportResponse missing = bySale with
        {
            SaleId = null,
            OrderId = Guid.NewGuid(),
        };

        Assert.Same(matchingSale, lookup.Invoke(host, [register, bySale]));
        Assert.Same(matchingSale, lookup.Invoke(host, [register, byOrder]));
        Assert.Null(lookup.Invoke(host, [register, missing]));
    }

    [Fact]
    public void A_split_ticket_has_one_row_per_payment_and_manual_movements_stay_separate()
    {
        Guid registerId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid ticketId = Guid.NewGuid();
        Guid cardPaymentId = Guid.NewGuid();
        Guid cashPaymentId = Guid.NewGuid();
        Guid expenseId = Guid.NewGuid();
        DateTime occurredAt = DateTime.UtcNow;
        CashRegisterReportResponse register = new(
            registerId,
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Closed",
            100m,
            210m,
            20m,
            40m,
            10m,
            150m,
            340m,
            340m,
            150m,
            0m,
            occurredAt,
            "TPV Centro",
            occurredAt.AddMinutes(5),
            "TPV Centro",
            [new CashRegisterPaymentMethodTotal("Card", 190m)],
            [new CashRegisterPaymentMethodExpected("Cash", 150m)],
            [new CashRegisterPaymentMethodReconciliation("Cash", 150m, 150m, 0m)],
            [
                new(
                    Guid.NewGuid(),
                    "Sale",
                    "Card",
                    190m,
                    "",
                    occurredAt,
                    "",
                    "TPV Centro",
                    null,
                    cardPaymentId,
                    ticketId
                ),
                new(
                    Guid.NewGuid(),
                    "Sale",
                    "Cash",
                    20m,
                    "",
                    occurredAt,
                    "",
                    "TPV Centro",
                    null,
                    cashPaymentId,
                    ticketId
                ),
                new(
                    expenseId,
                    "Expense",
                    "Cash",
                    10m,
                    "Factura #42",
                    occurredAt,
                    "Pago a proveedor",
                    "TPV Centro",
                    null
                ),
            ]
        );
        BackofficeHome host = new();
        SetField(host, "dailyCashRegisters", new List<CashRegisterReportResponse> { register });

        IRenderedComponent<CashRegistersByDayModule> cut = Render<CashRegistersByDayModule>(
            parameters => parameters.Add(component => component.Host, host)
        );

        Assert.Single(cut.FindAll(".cash-register-report-card"));
        Assert.Single(cut.FindAll("button[aria-label='Ver ticket de cierre']"));
        Assert.Equal(
            2,
            cut.FindAll(".cash-register-report-details")
                .Single(details => details.QuerySelector("summary")!.TextContent == "Tickets")
                .QuerySelectorAll(
                    ".cash-register-report-movement:not(.cash-register-report-movement-header)"
                )
                .Length
        );
        Assert.Contains($"Ticket {ticketId.ToString("N")[..8]}", cut.Markup);
        Assert.Contains($"Pago {cardPaymentId.ToString("N")[..8]}", cut.Markup);
        Assert.Contains($"Pago {cashPaymentId.ToString("N")[..8]}", cut.Markup);
        Assert.Contains("Tarjeta / datáfono", cut.Markup);
        Assert.Contains("Efectivo", cut.Markup);
        Assert.Contains($"Movimiento {expenseId.ToString("N")[..8]}", cut.Markup);
        Assert.Contains("Pago a proveedor · Factura #42", cut.Markup);
        Assert.All(
            cut.FindAll(
                ".cash-register-report-movement:not(.cash-register-report-movement-header)"
            ),
            row => Assert.DoesNotContain("TPV Centro", row.TextContent)
        );
        Assert.Contains("cash-register-negative", cut.Markup);
    }

    [Fact]
    public void Empty_daily_report_shows_its_empty_state()
    {
        BackofficeHome host = new();
        SetField(host, "dailyCashRegisters", new List<CashRegisterReportResponse>());
        SetField(host, "busy", false);

        IRenderedComponent<CashRegistersByDayModule> cut = Render<CashRegistersByDayModule>(
            parameters => parameters.Add(component => component.Host, host)
        );

        Assert.Contains("No hay cajas registradas para esta fecha y este local.", cut.Markup);
        Assert.Empty(cut.FindAll(".cash-register-report-card"));
    }

    [Fact]
    public void Open_register_without_reconciliation_hides_close_ticket_and_movement_sections()
    {
        CashRegisterReportResponse register = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            100m,
            0m,
            0m,
            0m,
            0m,
            100m,
            100m,
            null,
            null,
            null,
            DateTime.UtcNow,
            "TPV Centro",
            null,
            null,
            [],
            [],
            [],
            [
                new(
                    Guid.NewGuid(),
                    "CashChange",
                    "Cash",
                    5m,
                    "Cambio",
                    DateTime.UtcNow,
                    "",
                    "TPV Centro",
                    null
                ),
                new(
                    Guid.NewGuid(),
                    "CardTender",
                    "Card",
                    5m,
                    "Cobro",
                    DateTime.UtcNow,
                    "",
                    "TPV Centro",
                    null
                ),
            ]
        );
        BackofficeHome host = new();
        SetField(host, "dailyCashRegisters", new List<CashRegisterReportResponse> { register });

        IRenderedComponent<CashRegistersByDayModule> cut = Render<CashRegistersByDayModule>(
            parameters => parameters.Add(component => component.Host, host)
        );

        Assert.Contains("Abierta", cut.Markup);
        Assert.Contains("—", cut.Markup);
        Assert.Empty(cut.FindAll("button[aria-label='Ver ticket de cierre']"));
        Assert.Empty(cut.FindAll(".cash-register-report-details .cash-register-report-methods"));
        Assert.Empty(cut.FindAll(".cash-register-report-movements"));
        Assert.DoesNotContain("Ingresos y egresos registrados", cut.Markup);
        Assert.DoesNotContain("Tickets", cut.Markup);
    }

    private static void SetField(object component, string name, object value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private static async Task ShowSaleTicket(
        IRenderedComponent<BackofficeHome> cut,
        SaleResponse sale
    )
    {
        MethodInfo show = typeof(BackofficeHome).GetMethod(
            "ShowSaleTicket",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(async () => await (Task)show.Invoke(cut.Instance, [sale])!);
    }

    private static T GetField<T>(object component, string name) =>
        (T)
            component
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(component)!;

    private sealed class RecordingBackofficeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.PathAndQuery;
            Requests.Add(path);
            string body =
                path == "/api/identity/auth/provider"
                    ? "{\"activeProvider\":\"local\",\"microsoftConfigured\":false,\"googleConfigured\":false}"
                    : "[]";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
