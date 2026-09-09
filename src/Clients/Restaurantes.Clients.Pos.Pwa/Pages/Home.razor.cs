using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Pos.Pwa.Realtime;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Pages;

public partial class Home
{
    int? guestCount;
    const string SessionKey = "restaurantes.pos.login";
    string username = "camarero1.mad-centro";
    string password = "Camarero-01-1-2026!";
    string? message;
    bool busy;
    bool success;
    LoginResponse? login;
    Guid restaurantId;
    List<TableResponse> tables = [];
    string tableFilter = "All";
    string serviceMode = "DineIn";
    string quickPaymentMethod = "Card";
    string customerName = string.Empty;
    bool takeawayRequiresPrepayment = true;
    List<OrderResponse> activeOrders = [];
    List<OrderResponse> pickupOrders = [];
    TableResponse? selectedTable;
    DiningSessionResponse? session;
    List<MenuItemResponse> menu = [];
    Dictionary<Guid, int> quantities = [];
    Dictionary<Guid, string> notes = [];
    string? category;
    bool submittedLocally;
    string cancelReason = "Apertura accidental";
    SessionBillResponse? bill;
    bool billOpen;
    string paymentMethod = "Card";
    string paymentReference = string.Empty;
    Guid checkoutKey;
    Dictionary<Guid, OrderDetailResponse> billOrders = [];
    CancellationTokenSource? realtimeRefresh;
    CancellationTokenSource? reconciliation;
    CashRegisterResponse? cashRegister;
    bool cashPanelOpen;
    decimal openingFloat;
    Dictionary<string, decimal> reconciliationAmounts = new(StringComparer.OrdinalIgnoreCase);

    bool CashRegisterOpen => cashRegister?.Status == "Open";
    decimal ReconciliationDifference =>
        reconciliationAmounts.Values.Sum() - (cashRegister?.ExpectedTotal ?? 0);

    List<TableResponse> FilteredTables =>
        tables.Where(x => tableFilter == "All" || x.Status == tableFilter).ToList();
    List<string> Categories => menu.Select(x => x.CategoryName).Distinct().Order().ToList();
    List<MenuItemResponse> VisibleMenu =>
        menu.Where(x => x.IsAvailable && (category is null || x.CategoryName == category)).ToList();
    List<MenuItemResponse> Cart =>
        menu.Where(x => quantities.GetValueOrDefault(x.ProductId) > 0).ToList();
    int ItemCount => quantities.Values.Sum();
    decimal Total => menu.Sum(x => x.Price * quantities.GetValueOrDefault(x.ProductId));
    bool HasPhysicalLocation => serviceMode is "DineIn" or "Bar";
    bool CanCancelSession =>
        HasPhysicalLocation
        && session is not null
        && session.Orders.Count == 0
        && !submittedLocally;
    string CurrentLabel =>
        HasPhysicalLocation ? selectedTable?.Label ?? "Ubicación" : "Pedido para llevar";

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        DiningRealtime.TableChanged += HandleTableChanged;
        string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            login = JsonSerializer.Deserialize<LoginResponse>(json);
            if (
                login is null
                || login.ExpiresAtUtc <= DateTime.UtcNow
                || login.Restaurants.Count == 0
            )
            {
                await Logout();
                return;
            }
            Api.AccessToken = login.AccessToken;
            restaurantId = login.Restaurants[0].RestaurantId;
            await LoadTables();
            await ConnectRealtimeAsync();
            StartReconciliation();
        }
        catch (JsonException)
        {
            await Logout();
        }
    }

    async Task DoLogin() =>
        await Run(async () =>
        {
            login = await Api.LoginAsync(username, password);
            RestaurantAccess? allowed = login.Restaurants.FirstOrDefault(x =>
                x.Permissions.Contains("orders.create")
                && x.Permissions.Contains("payments.capture")
            );
            if (allowed is null)
                throw new InvalidOperationException(
                    "El usuario no tiene permisos para operar el TPV."
                );
            Api.AccessToken = login.AccessToken;
            restaurantId = allowed.RestaurantId;
            await Js.InvokeVoidAsync(
                "sessionStorage.setItem",
                SessionKey,
                JsonSerializer.Serialize(login)
            );
            await LoadOperationalData();
            await ConnectRealtimeAsync();
            StartReconciliation();
        });

    async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        reconciliation = null;
        Api.AccessToken = null;
        login = null;
        cashRegister = null;
        tables = [];
        BackToTables();
        message = null;
    }

    async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out Guid value))
            return;
        restaurantId = value;
        BackToTables();
        await LoadTables();
        await ConnectRealtimeAsync();
    }

    async Task LoadTables() => await Run(LoadOperationalData);

    async Task LoadOperationalData()
    {
        tables = await Api.TablesAsync(restaurantId);
        DiningPolicyResponse policy = await Api.PolicyAsync(restaurantId);
        takeawayRequiresPrepayment = policy.TakeawayRequiresPrepayment;
        cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
        PrepareReconciliation();
        await LoadPickupOrdersCore();
    }

    async Task LoadPickupOrders() => await Run(LoadPickupOrdersCore);

    async Task LoadPickupOrdersCore()
    {
        activeOrders = await Api.OrdersAsync(restaurantId);
        pickupOrders = activeOrders
            .Where(x => x.ServiceMode == "Takeaway")
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
    }

    async Task SelectTable(TableResponse table) =>
        await Run(async () =>
        {
            if (!CashRegisterOpen && table.Status != "Occupied")
            {
                message = "Debes abrir la caja antes de ocupar una mesa.";
                return;
            }
            serviceMode = "DineIn";
            session = table.ActiveSessionId.HasValue
                ? await Api.ActiveSessionAsync(table.Id)
                : await Api.OpenSessionAsync(table.Id);
            guestCount = session.GuestCount;
            selectedTable = table;
            submittedLocally = session.Orders.Count > 0;
            menu = await Api.MenuAsync(restaurantId);
            quantities = menu.ToDictionary(x => x.ProductId, _ => 0);
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });

    async Task BeginTakeaway() =>
        await Run(async () =>
        {
            if (!CashRegisterOpen)
            {
                message = "Debes abrir la caja antes de iniciar un pedido.";
                return;
            }
            serviceMode = "Takeaway";
            customerName = string.Empty;
            selectedTable = null;
            session = null;
            menu = await Api.MenuAsync(restaurantId);
            quantities = menu.ToDictionary(x => x.ProductId, _ => 0);
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });

    void ChangeQuantity(Guid productId, int change) =>
        quantities[productId] = Math.Clamp(quantities[productId] + change, 0, 99);

    async Task SendOrder()
    {
        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de tomar pedidos.";
            return;
        }
        if (HasPhysicalLocation && (selectedTable is null || session is null))
            return;
        if (!HasPhysicalLocation && string.IsNullOrWhiteSpace(customerName))
            return;
        if (
            !HasPhysicalLocation
            && takeawayRequiresPrepayment
            && quickPaymentMethod == "Cash"
            && !CashRegisterOpen
        )
        {
            message = "Debes abrir la caja diaria antes de cobrar en efectivo.";
            return;
        }
        if (!HasPhysicalLocation && takeawayRequiresPrepayment)
        {
            bool confirmed = await Js.InvokeAsync<bool>(
                "confirm",
                $"Confirma que el cobro de {Total:0.00} € mediante {PaymentLabel(quickPaymentMethod)} fue aceptado. Esta acción registrará el pedido como PAGADO."
            );
            if (!confirmed)
                return;
        }

        await Run(
            async () =>
            {
                OrderResponse order;
                if (HasPhysicalLocation)
                {
                    if (session is { RequestGuestCount: true, GuestCount: null })
                        session = await Api.SetGuestCountAsync(session.Id, guestCount ?? 0);
                    order = await Api.CreateAndSubmitAsync(
                        restaurantId,
                        selectedTable!,
                        session!.Id,
                        serviceMode,
                        quantities,
                        notes
                    );
                }
                else if (takeawayRequiresPrepayment)
                {
                    order = await Api.CreatePayAndSubmitQuickSaleAsync(
                        restaurantId,
                        serviceMode,
                        customerName.Trim(),
                        quickPaymentMethod,
                        cashRegister?.Id,
                        quantities,
                        notes
                    );
                }
                else
                {
                    order = await Api.CreateAndSubmitTakeawayOnAccountAsync(
                        restaurantId,
                        customerName.Trim(),
                        quantities,
                        notes
                    );
                }

                foreach (Guid id in quantities.Keys.ToArray())
                {
                    quantities[id] = 0;
                    notes[id] = string.Empty;
                }
                submittedLocally = HasPhysicalLocation;
                success = true;
                message =
                    HasPhysicalLocation ? $"Comanda {order.Id.ToString()[..8]} enviada a cocina."
                    : takeawayRequiresPrepayment
                        ? $"Recogida {order.Id.ToString()[..8]} pagada y enviada para {order.CustomerName}."
                    : $"Recogida {order.Id.ToString()[..8]} enviada para {order.CustomerName}; cobro pendiente.";
                if (!HasPhysicalLocation)
                {
                    BackToTables();
                    await Task.Delay(400);
                    await LoadPickupOrdersCore();
                }
            },
            false
        );
    }

    async Task CompletePickup(OrderResponse pickup)
    {
        bool requiresPayment = pickup.PaymentTiming != "Immediate";
        if (requiresPayment && !CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de registrar el cobro.";
            return;
        }
        string prompt = requiresPayment
            ? $"Confirma el cobro de {pickup.Total:0.00} € mediante {PaymentLabel(quickPaymentMethod)} y la entrega a {pickup.CustomerName}."
            : $"Confirma que entregas el pedido a {pickup.CustomerName}.";
        if (!await Js.InvokeAsync<bool>("confirm", prompt))
            return;

        await Run(
            async () =>
            {
                await Api.DeliverPickupAsync(
                    pickup.Id,
                    requiresPayment ? quickPaymentMethod : null,
                    cashRegister?.Id
                );
                success = true;
                message = requiresPayment
                    ? $"Pedido cobrado y entregado a {pickup.CustomerName}."
                    : $"Pedido entregado a {pickup.CustomerName}.";
                await Task.Delay(400);
                await LoadPickupOrdersCore();
            },
            false
        );
    }

    async Task CancelSession()
    {
        if (session is null)
            return;
        await Run(
            async () =>
            {
                await Api.CancelSessionAsync(session.Id, cancelReason);
                success = true;
                message = "Apertura cancelada y ubicación liberada.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    async Task OpenBill()
    {
        if (session is null)
            return;
        billOpen = true;
        await Run(LoadBill);
    }

    async Task LoadBill()
    {
        if (session is null)
            return;
        bill = await Api.BillAsync(session.Id);
        OrderDetailResponse[] details = await Task.WhenAll(
            bill.Orders.Select(order => Api.OrderAsync(order.OrderId))
        );
        billOrders = details.ToDictionary(order => order.Id);
    }

    static bool CanCancelLine(
        SessionBillOrderResponse billOrder,
        OrderDetailResponse order,
        OrderLineDetailResponse line
    ) =>
        billOrder.PaymentStatus != "Paid"
        && line.Status == "Active"
        && OrderProgress.CanCancelBeforePreparation(order.Status)
        && order.Stations.Any(station =>
            station.Code == line.PreparationStationCode && station.Status == "Pending"
        );

    async Task CancelLine(SessionBillOrderResponse order, OrderLineDetailResponse line)
    {
        string? reason = await Js.InvokeAsync<string?>(
            "prompt",
            $"Motivo para cancelar {line.Quantity} × {line.ProductName}:"
        );
        if (string.IsNullOrWhiteSpace(reason))
            return;
        bool confirmed = await Js.InvokeAsync<bool>(
            "confirm",
            "La línea desaparecerá del KDS si cocina todavía no comenzó. ¿Continuar?"
        );
        if (!confirmed)
            return;
        await Run(
            async () =>
            {
                await Api.CancelOrderLineAsync(order.OrderId, line.Id, reason.Trim());
                await Task.Delay(500);
                await LoadBill();
                success = true;
                message = $"{line.ProductName} cancelado antes de preparación.";
            },
            false
        );
    }

    void CloseBill() => billOpen = false;

    async Task CheckoutSession()
    {
        if (session is null || busy)
            return;
        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de registrar el cobro.";
            return;
        }
        checkoutKey = checkoutKey == Guid.Empty ? Guid.NewGuid() : checkoutKey;
        await Run(
            async () =>
            {
                string reference = PosApi.PaymentReference(
                    paymentMethod,
                    cashRegister?.Id,
                    paymentReference
                );
                bill = await Api.CheckoutAsync(session.Id, checkoutKey, paymentMethod, reference);
                success = true;
                message = $"Cobro completado: {bill.Total:0.00} €. Ubicación liberada.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    async Task ReleaseSession()
    {
        if (session is null)
            return;
        await Run(
            async () =>
            {
                await Api.ReleaseSessionAsync(session.Id);
                success = true;
                message = "Mesa liberada desde TPV.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    void BackToTables()
    {
        selectedTable = null;
        session = null;
        serviceMode = "DineIn";
        menu = [];
        quantities = [];
        notes = [];
        category = null;
        submittedLocally = false;
        cancelReason = "Apertura accidental";
        bill = null;
        billOrders = [];
        billOpen = false;
        checkoutKey = Guid.Empty;
        paymentReference = string.Empty;
    }

    async Task OpenCashRegisterPanel()
    {
        cashPanelOpen = true;
        await RefreshCashRegister();
        PrepareReconciliation();
    }

    void CloseCashRegisterPanel() => cashPanelOpen = false;

    async Task RefreshCashRegister() =>
        await Run(
            async () =>
            {
                cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
                PrepareReconciliation();
            },
            false
        );

    async Task OpenDailyCashRegister() =>
        await Run(
            async () =>
            {
                cashRegister = await Api.OpenCashRegisterAsync(restaurantId, openingFloat);
                PrepareReconciliation();
                success = true;
                message = $"Caja diaria abierta con {openingFloat:0.00} € de fondo.";
            },
            false
        );

    async Task CloseDailyCashRegister()
    {
        if (cashRegister is null)
            return;
        decimal reconciledTotal = reconciliationAmounts.Values.Sum();
        if (
            !await Js.InvokeAsync<bool>(
                "confirm",
                $"Total esperado: {cashRegister.ExpectedTotal:0.00} €. Total conciliado: {reconciledTotal:0.00} €. ¿Cerrar el turno?"
            )
        )
            return;
        await Run(
            async () =>
            {
                cashRegister = await Api.CloseCashRegisterAsync(
                    restaurantId,
                    cashRegister.Id,
                    reconciliationAmounts
                );
                success = true;
                message = $"Caja cerrada. Descuadre: {cashRegister.Difference:+0.00;-0.00;0.00} €.";
            },
            false
        );
    }

    void PrepareReconciliation()
    {
        reconciliationAmounts =
            cashRegister?.ExpectedByMethod.ToDictionary(
                x => x.Method,
                x => x.Expected,
                StringComparer.OrdinalIgnoreCase
            ) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    string FilterClass(string value) => tableFilter == value ? "filter active" : "filter";

    string CategoryClass(string? value) => category == value ? "filter active" : "filter";

    static string StatusLabel(string status) => status == "Occupied" ? "Ocupada" : "Disponible";

    string TableOrderSummary(TableResponse table)
    {
        List<OrderResponse> orders = activeOrders.Where(x => x.TableId == table.Id).ToList();
        return OrderProgress.Summarize(orders.Select(x => x.Status));
    }

    string SessionOrderSummary(Guid sessionId) =>
        OrderProgress.Summarize(
            activeOrders.Where(x => x.DiningSessionId == sessionId).Select(x => x.Status)
        );

    static string PaymentLabel(string method) =>
        method switch
        {
            "Cash" => "Efectivo",
            "Card" => "Tarjeta / datáfono",
            "Online" => "Pago online",
            "Cheque" => "Cheque",
            _ => method,
        };

    static string ModeLabel(string mode) =>
        mode switch
        {
            "Bar" => "BARRA",
            "Takeaway" => "PARA LLEVAR",
            _ => "SERVICIO EN MESA",
        };

    async Task Run(Func<Task> action, bool clearMessage = true)
    {
        if (clearMessage)
            message = null;
        success = false;
        busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    async Task ConnectRealtimeAsync()
    {
        try
        {
            await Realtime.ConnectAsync(restaurantId);
            await DiningRealtime.ConnectAsync(restaurantId);
            tables = await Api.TablesAsync(restaurantId);
            StateHasChanged();
        }
        catch (Exception exception)
        {
            message = $"Datos cargados, pero sin actualización en tiempo real: {exception.Message}";
        }
    }

    Task HandleOrderUpdated(OrderRealtimeNotification notification)
    {
        return ScheduleRealtimeRefresh(notification.RestaurantId);
    }

    async Task HandleTableChanged(DiningTableChangedNotification notification)
    {
        if (login is null || notification.RestaurantId != restaurantId)
            return;

        await InvokeAsync(() =>
        {
            int index = tables.FindIndex(table => table.Id == notification.TableId);
            if (index >= 0)
            {
                tables[index] = tables[index] with
                {
                    Status = notification.Status,
                    ActiveSessionId = notification.ActiveSessionId,
                };
                StateHasChanged();
            }
        });

        await ScheduleRealtimeRefresh(notification.RestaurantId);
    }

    Task ScheduleRealtimeRefresh(Guid changedRestaurantId)
    {
        if (login is null || changedRestaurantId != restaurantId)
            return Task.CompletedTask;
        realtimeRefresh?.Cancel();
        realtimeRefresh?.Dispose();
        realtimeRefresh = new CancellationTokenSource();
        _ = RefreshAfterOrderEventAsync(realtimeRefresh.Token);
        return Task.CompletedTask;
    }

    async Task RefreshAfterOrderEventAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await InvokeAsync(async () =>
            {
                tables = await Api.TablesAsync(restaurantId);
                await LoadPickupOrdersCore();
                if (session is not null)
                {
                    TableResponse? current = tables.FirstOrDefault(x => x.Id == session.TableId);
                    if (current?.ActiveSessionId == session.Id)
                    {
                        selectedTable = current;
                        session = await Api.ActiveSessionAsync(current.Id);
                        if (billOpen)
                        {
                            await LoadBill();
                        }
                    }
                    else
                    {
                        BackToTables();
                    }
                }
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                message = $"No se pudo actualizar el TPV en tiempo real: {exception.Message}";
                StateHasChanged();
            });
        }
    }

    void StartReconciliation()
    {
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        reconciliation = new CancellationTokenSource();
        _ = ReconcileOperationalDataAsync(reconciliation.Token);
    }

    async Task ReconcileOperationalDataAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (login is null || busy)
                        return;
                    tables = await Api.TablesAsync(restaurantId);
                    await LoadPickupOrdersCore();
                    cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
                    PrepareReconciliation();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                message =
                    $"No se pudo reconciliar el TPV con la base de datos: {exception.Message}";
                StateHasChanged();
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Realtime.OrderUpdated -= HandleOrderUpdated;
        DiningRealtime.TableChanged -= HandleTableChanged;
        realtimeRefresh?.Cancel();
        realtimeRefresh?.Dispose();
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
    }
}
