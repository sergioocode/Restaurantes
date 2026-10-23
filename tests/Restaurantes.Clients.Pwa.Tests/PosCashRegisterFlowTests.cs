using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Pos.Pwa.Realtime;
using Xunit;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosCashRegisterFlowTests : BunitContext
{
    private readonly Guid restaurantId = Guid.NewGuid();
    private readonly Guid registerId = Guid.NewGuid();

    [Fact]
    public async Task Opening_a_register_with_the_keypad_returns_to_the_pos_without_opening_the_summary()
    {
        CashRegisterResponse opened = Register("Open", 125.50m);
        using CashRegisterHandler handler = new(opened);
        RecordingJsRuntime js = new();
        IRenderedComponent<PosHome> cut = RenderHome(handler, js);
        SetField(cut.Instance, "cashPanelOpen", true);
        await Rerender(cut);

        foreach (string digit in new[] { "1", "2", "5", ",", "5", "0" })
        {
            cut.Find($".cash-opening-keypad button[aria-label='{digit}']").Click();
        }

        Assert.Equal("125,50", GetField<string>(cut.Instance, "openingFloatKeypadText"));
        Assert.Equal(125.50m, GetField<decimal>(cut.Instance, "openingFloat"));
        await cut.FindAll("button")
            .Single(button => button.TextContent == "Abrir turno de caja")
            .ClickAsync();

        Assert.Equal(125.50m, handler.OpeningFloat);
        Assert.Contains("Caja · abierta", cut.Markup);
        Assert.Empty(cut.FindAll(".cash-register-card"));
        Assert.Equal(
            $"Caja diaria abierta con {125.50m:0.00} € de fondo.",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Opening_float_keypad_rejects_duplicate_separators_and_extra_decimal_places()
    {
        using CashRegisterHandler handler = new(Register("Open", 0m));
        IRenderedComponent<PosHome> cut = RenderHome(handler, new RecordingJsRuntime());
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(cut.Instance, "openingFloatKeypadText", "10,25");
        SetField(cut.Instance, "openingFloat", 10.25m);
        SetField(cut.Instance, "openingFloatKeypadStartNew", false);
        await Rerender(cut);

        await cut.Find(".cash-opening-keypad button[aria-label=',']").ClickAsync();
        await cut.Find(".cash-opening-keypad button[aria-label='9']").ClickAsync();

        Assert.Equal("10,25", GetField<string>(cut.Instance, "openingFloatKeypadText"));
        Assert.Equal(10.25m, GetField<decimal>(cut.Instance, "openingFloat"));

        await cut.Find(".cash-opening-keypad button[aria-label='Borrar todo el importe']")
            .ClickAsync();
        await cut.Find(".cash-opening-keypad button[aria-label=',']").ClickAsync();
        await cut.Find(".cash-opening-keypad button[aria-label='5']").ClickAsync();

        Assert.Equal("0,5", GetField<string>(cut.Instance, "openingFloatKeypadText"));
        Assert.Equal(0.5m, GetField<decimal>(cut.Instance, "openingFloat"));
    }

    [Fact]
    public async Task Closing_a_register_shows_the_ticket_and_prints_only_when_requested()
    {
        CashRegisterResponse opened = Register("Open", 200m);
        CashRegisterResponse closed = opened with
        {
            Status = "Closed",
            ReconciledTotal = 200m,
            CountedCash = 200m,
            Difference = 0m,
            ClosedAtUtc = DateTime.UtcNow,
            ClosedByName = "TPV Centro",
            Reconciliations = [new PaymentMethodReconciliation("Cash", 200m, 200m, 0m)],
        };
        using CashRegisterHandler handler = new(opened, closed);
        RecordingJsRuntime js = new();
        IRenderedComponent<PosHome> cut = RenderHome(handler, js);
        SetField(cut.Instance, "cashRegister", opened);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Cash"] = 200m }
        );
        await Rerender(cut);

        await cut.FindAll("button")
            .Single(button => button.TextContent == "Cerrar y confirmar arqueo")
            .ClickAsync();

        Assert.Equal(1, js.ConfirmationCount);
        Assert.Equal(200m, handler.ReconciledCash);
        Assert.Equal(0, js.PrintCount);
        Assert.Contains("Arqueo cerrado", cut.Markup);
        Assert.Contains("cash-close-ticket-preview", cut.Markup);
        Assert.DoesNotContain(
            cut.FindAll("button"),
            button => button.TextContent == "Cerrar y confirmar arqueo"
        );

        await cut.Find("button[aria-label='Imprimir cierre de caja']").ClickAsync();
        Assert.Equal(1, js.PrintCount);

        cut.Find("button[aria-label='Salir del ticket de cierre']").Click();
        Assert.Empty(cut.FindAll(".cash-register-card"));
        Assert.Contains("Caja · cerrada", cut.Markup);

        await cut.Find(".cash-status").ClickAsync();
        Assert.Contains("Abrir turno", cut.Markup);
        Assert.DoesNotContain("cash-close-ticket-preview", cut.Markup);
    }

    [Fact]
    public async Task Manual_income_and_expense_are_recorded_separately_with_their_own_signs()
    {
        CashRegisterResponse opened = Register("Open", 100m);
        using CashRegisterHandler handler = new(opened);
        IRenderedComponent<PosHome> cut = RenderHome(handler, new RecordingJsRuntime());
        SetField(cut.Instance, "cashRegister", opened);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Cash"] = 100m }
        );
        await Rerender(cut);
        cut.WaitForAssertion(() => Assert.Contains("Turno abierto", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent == "Ingresos y egresos").Click();
        cut.Find(".cash-movement-type-options button:first-child").Click();
        cut.FindAll(".cash-movement-category-options button")
            .Single(button => button.TextContent == "Devolución de proveedor")
            .Click();
        cut.Find(".cash-movement-fields input[type='number']").Change("40");
        cut.Find(".cash-movement-fields input[maxlength]").Change("Factura devuelta");
        await cut.FindAll("button")
            .Single(button => button.TextContent == "Registrar movimiento")
            .ClickAsync();

        Assert.Equal(
            ("Income", "Devolución de proveedor", 40m, "Factura devuelta"),
            handler.CreatedMovements[0]
        );
        Assert.Contains("+40", cut.Markup);
        Assert.Contains("Devolución de proveedor", cut.Markup);
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "cashMovementAmount"));
        Assert.Equal(string.Empty, GetField<string>(cut.Instance, "cashMovementDescription"));

        cut.Find(".cash-movement-type-options button:last-child").Click();
        cut.Find(".cash-movement-fields input[type='number']").Change("10");
        cut.Find(".cash-movement-fields input[maxlength]").Change("Pago de factura");
        await cut.FindAll("button")
            .Single(button => button.TextContent == "Registrar movimiento")
            .ClickAsync();

        Assert.Equal(
            ("Expense", "Pago a proveedor", 10m, "Pago de factura"),
            handler.CreatedMovements[1]
        );
        Assert.Equal(2, cut.FindAll(".cash-movement-row").Count);
        Assert.Contains("−10", cut.Markup);
    }

    [Fact]
    public async Task Declining_the_confirmation_keeps_the_register_open()
    {
        CashRegisterResponse opened = Register("Open", 100m);
        using CashRegisterHandler handler = new(opened, opened with { Status = "Closed" });
        RecordingJsRuntime js = new() { ConfirmResult = false };
        IRenderedComponent<PosHome> cut = RenderHome(handler, js);
        SetField(cut.Instance, "cashRegister", opened);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Cash"] = 100m }
        );
        await Rerender(cut);

        await cut.FindAll("button")
            .Single(button => button.TextContent == "Cerrar y confirmar arqueo")
            .ClickAsync();

        Assert.Equal(1, js.ConfirmationCount);
        Assert.Null(handler.ReconciledCash);
        Assert.Contains("Turno abierto", cut.Markup);
        Assert.DoesNotContain("cash-close-ticket-preview", cut.Markup);
    }

    [Fact]
    public async Task Opening_failure_keeps_the_opening_form_and_shows_the_api_error()
    {
        CashRegisterHandler handler = new(Register("Open", 0m)) { FailOpen = true };
        IRenderedComponent<PosHome> cut = RenderHome(handler, new RecordingJsRuntime());
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(cut.Instance, "openingFloat", 75m);
        SetField(cut.Instance, "openingFloatKeypadText", "75");
        await Rerender(cut);

        await cut.FindAll("button")
            .Single(button => button.TextContent == "Abrir turno de caja")
            .ClickAsync();

        Assert.Contains("No se pudo abrir la caja", cut.Markup);
        Assert.Contains("Abrir turno", cut.Markup);
        Assert.Equal(75m, GetField<decimal>(cut.Instance, "openingFloat"));
    }

    [Fact]
    public async Task Closing_failure_keeps_the_open_register_and_does_not_show_a_ticket()
    {
        CashRegisterResponse opened = Register("Open", 100m);
        using CashRegisterHandler handler = new(opened, opened with { Status = "Closed" })
        {
            FailClose = true,
        };
        IRenderedComponent<PosHome> cut = RenderHome(handler, new RecordingJsRuntime());
        SetField(cut.Instance, "cashRegister", opened);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Cash"] = 100m }
        );
        await Rerender(cut);

        await cut.FindAll("button")
            .Single(button => button.TextContent == "Cerrar y confirmar arqueo")
            .ClickAsync();

        Assert.Contains("No se pudo cerrar la caja", cut.Markup);
        Assert.Contains("Turno abierto", cut.Markup);
        Assert.DoesNotContain("cash-close-ticket-preview", cut.Markup);
    }

    private IRenderedComponent<PosHome> RenderHome(
        CashRegisterHandler handler,
        RecordingJsRuntime js
    )
    {
        HttpClient http = new(handler) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton(new CatalogRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime>(js);
        IRenderedComponent<PosHome> cut = Render<PosHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                []
            )
        );
        SetField(cut.Instance, "initializing", false);
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(
            cut.Instance,
            "restaurant",
            new RestaurantResponse(restaurantId, "CENTRO", "Local Centro", "Calle Mayor", true)
        );
        return cut;
    }

    private CashRegisterResponse Register(string status, decimal openingFloat) =>
        new(
            registerId,
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            status,
            openingFloat,
            0m,
            0m,
            0m,
            0m,
            openingFloat,
            openingFloat,
            null,
            null,
            null,
            DateTime.UtcNow,
            "TPV Centro",
            null,
            null,
            [],
            [new PaymentMethodExpected("Cash", openingFloat)],
            [],
            []
        );

    private static Task Rerender(IRenderedComponent<PosHome> cut) =>
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.Empty));

    private static void SetField(object component, string name, object value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private static T GetField<T>(object component, string name) =>
        (T)
            component
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(component)!;

    private sealed class CashRegisterHandler(
        CashRegisterResponse opened,
        CashRegisterResponse? closed = null
    ) : HttpMessageHandler
    {
        private CashRegisterResponse current = opened;

        public decimal? OpeningFloat { get; private set; }
        public decimal? ReconciledCash { get; private set; }
        public bool FailOpen { get; init; }
        public bool FailClose { get; init; }
        public List<(
            string Type,
            string Category,
            decimal Amount,
            string Description
        )> CreatedMovements { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/current", StringComparison.Ordinal)
            )
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/open", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                OpeningFloat = body.RootElement.GetProperty("openingFloat").GetDecimal();
                if (FailOpen)
                {
                    return Error("No se pudo abrir la caja");
                }
                return Json(current);
            }

            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/close", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                ReconciledCash = body
                    .RootElement.GetProperty("reconciledByMethod")
                    .GetProperty("Cash")
                    .GetDecimal();
                if (FailClose)
                {
                    return Error("No se pudo cerrar la caja");
                }
                return Json(closed!);
            }

            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/movements", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                string type = body.RootElement.GetProperty("type").GetString()!;
                string category = body.RootElement.GetProperty("category").GetString()!;
                decimal amount = body.RootElement.GetProperty("amount").GetDecimal();
                string description = body.RootElement.GetProperty("description").GetString()!;
                CreatedMovements.Add((type, category, amount, description));
                current = current with
                {
                    OtherIncomeTotal = current.OtherIncomeTotal + (type == "Income" ? amount : 0),
                    OtherExpenseTotal =
                        current.OtherExpenseTotal + (type == "Expense" ? amount : 0),
                    Movements =
                    [
                        .. current.Movements,
                        new CashMovementResponse(
                            Guid.NewGuid(),
                            type,
                            "Cash",
                            amount,
                            description,
                            DateTime.UtcNow,
                            category,
                            "TPV Centro"
                        ),
                    ],
                };
                return Json(current);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(CashRegisterResponse response) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(response) };

        private static HttpResponseMessage Error(string detail) =>
            new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { detail }) };
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public int ConfirmationCount { get; private set; }
        public int PrintCount { get; private set; }
        public bool ConfirmResult { get; init; } = true;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            if (identifier == "confirm")
            {
                ConfirmationCount++;
                return ValueTask.FromResult((TValue)(object)ConfirmResult);
            }

            if (identifier == "window.print")
            {
                PrintCount++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
