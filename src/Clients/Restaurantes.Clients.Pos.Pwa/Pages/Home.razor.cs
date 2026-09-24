using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Pages;

public partial class Home
{
    private int? guestCount;
    private const string SessionKey = "restaurantes.pos.login";
    private string? message;
    private bool busy;
    private bool success;
    private bool initializing = true;
    private LoginResponse? login;
    private ProviderSettingsResponse? providerSettings;
    private Guid restaurantId;
    private List<RestaurantResponse> restaurants = [];
    private List<TableResponse> tables = [];
    private string tableFilter = "All";
    private string serviceMode = "DineIn";
    private string quickPaymentMethod = "Card";
    private string customerName = string.Empty;
    private bool takeawayRequiresPrepayment = true;
    private List<OrderResponse> activeOrders = [];
    private List<OrderResponse> pickupOrders = [];
    private TableResponse? selectedTable;
    private DiningSessionResponse? session;
    private List<MenuItemResponse> menu = [];
    private Dictionary<Guid, int> quantities = [];
    private Dictionary<Guid, string> notes = [];
    private string? category;
    private SessionBillResponse? bill;
    private bool billOpen;
    private string paymentMethod = "Card";
    private string paymentReference = string.Empty;
    private Guid checkoutKey;
    private Dictionary<Guid, OrderDetailResponse> billOrders = [];
    private CancellationTokenSource? realtimeRefresh;
    private CancellationTokenSource? reconciliation;
    private CashRegisterResponse? cashRegister;
    private bool cashPanelOpen;
    private decimal openingFloat;
    private Dictionary<string, decimal> reconciliationAmounts = new(
        StringComparer.OrdinalIgnoreCase
    );

    private bool CashRegisterOpen => cashRegister?.Status == "Open";
    private decimal ReconciliationDifference =>
        reconciliationAmounts.Values.Sum() - (cashRegister?.ExpectedTotal ?? 0);

    private List<TableResponse> FilteredTables =>
        tables.Where(x => tableFilter == "All" || x.Status == tableFilter).ToList();
    private List<string> Categories => menu.Select(x => x.CategoryName).Distinct().Order().ToList();
    private List<MenuItemResponse> VisibleMenu =>
        menu.Where(x => x.IsAvailable && (category is null || x.CategoryName == category)).ToList();
    private List<MenuItemResponse> Cart =>
        menu.Where(x => quantities.GetValueOrDefault(x.ProductId) > 0).ToList();
    private int ItemCount => quantities.Values.Sum();
    private decimal Total => menu.Sum(x => x.Price * quantities.GetValueOrDefault(x.ProductId));
    private bool HasPhysicalLocation => serviceMode is "DineIn" or "Bar";
    private bool hasSessionOrders;
    private bool CanCancelSession => HasPhysicalLocation && session is not null;
    private string CurrentLabel =>
        HasPhysicalLocation ? selectedTable?.Label ?? "Ubicación" : "Pedido para llevar";

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        DiningRealtime.TableChanged += HandleTableChanged;
        try
        {
            providerSettings = await Api.ProviderAsync();
            string? code = QueryValue("login_code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                await Js.InvokeVoidAsync("history.replaceState", null, "", Nav.BaseUri);
                await AcceptLogin(await Api.ExchangeAsync(code));
                return;
            }
            if (QueryValue("login_error") is not null)
            {
                await Js.InvokeVoidAsync("history.replaceState", null, "", Nav.BaseUri);
                message = "La cuenta no está autorizada o el proveedor rechazó el acceso.";
            }

            string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            login = JsonSerializer.Deserialize<LoginResponse>(json);
            if (login is null || login.ExpiresAtUtc <= DateTime.UtcNow)
            {
                await Logout();
                return;
            }
            Api.AccessToken = login.AccessToken;
            await LoadRestaurants();
            await LoadTables();
            await ConnectRealtimeAsync();
            StartReconciliation();
        }
        catch (Exception exception)
        {
            message = exception.Message;
        }
        finally
        {
            initializing = false;
        }
    }

    private void StartLogin(string provider)
    {
        Nav.NavigateTo(
            $"/api/identity/auth/start/{provider}?returnPath=%2Fpos%2F",
            forceLoad: true
        );
    }

    private string? QueryValue(string key)
    {
        string query = new Uri(Nav.Uri).Fragment.TrimStart('#');
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair[0] == key)
            {
                return Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : "");
            }
        }
        return null;
    }

    private async Task AcceptLogin(LoginResponse response)
    {
        login = response;
        Api.AccessToken = login.AccessToken;
        await Js.InvokeVoidAsync(
            "sessionStorage.setItem",
            SessionKey,
            JsonSerializer.Serialize(login)
        );
        await LoadRestaurants();
        await LoadOperationalData();
        await ConnectRealtimeAsync();
        StartReconciliation();
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        reconciliation = null;
        Api.AccessToken = null;
        login = null;
        restaurants = [];
        cashRegister = null;
        tables = [];
        BackToTables();
        message = null;
    }

    private async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out Guid value))
        {
            return;
        }

        restaurantId = value;
        BackToTables();
        await LoadTables();
        await ConnectRealtimeAsync();
    }

    private async Task LoadTables()
    {
        await Run(LoadOperationalData);
    }

    private async Task LoadRestaurants()
    {
        restaurants = await Api.RestaurantsAsync();
        if (restaurants.Count == 0)
        {
            throw new InvalidOperationException(
                "El usuario no tiene permisos para operar el TPV en ningún local."
            );
        }

        if (!restaurants.Any(restaurant => restaurant.Id == restaurantId))
        {
            restaurantId = restaurants[0].Id;
        }
    }

    private async Task LoadOperationalData()
    {
        tables = await Api.TablesAsync(restaurantId);
        DiningPolicyResponse policy = await Api.PolicyAsync(restaurantId);
        takeawayRequiresPrepayment = policy.TakeawayRequiresPrepayment;
        cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
        PrepareReconciliation();
        await LoadPickupOrdersCore();
    }

    private async Task LoadPickupOrders()
    {
        await Run(LoadPickupOrdersCore);
    }

    private async Task LoadPickupOrdersCore()
    {
        activeOrders = await Api.OrdersAsync(restaurantId);
        pickupOrders = activeOrders
            .Where(x => x.ServiceMode == "Takeaway")
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
    }

    private async Task SelectTable(TableResponse table)
    {
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
            hasSessionOrders = session.Orders.Count > 0;
            menu = await Api.MenuAsync(restaurantId);
            quantities = menu.ToDictionary(x => x.ProductId, _ => 0);
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });
    }

    private async Task BeginTakeaway()
    {
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
    }

    private void ChangeQuantity(Guid productId, int change)
    {
        quantities[productId] = Math.Clamp(quantities[productId] + change, 0, 99);
    }

    private async Task SendOrder()
    {
        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de tomar pedidos.";
            return;
        }
        if (HasPhysicalLocation && (selectedTable is null || session is null))
        {
            return;
        }

        if (!HasPhysicalLocation && string.IsNullOrWhiteSpace(customerName))
        {
            return;
        }

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
            {
                return;
            }
        }

        await Run(
            async () =>
            {
                OrderResponse order;
                if (HasPhysicalLocation)
                {
                    if (session is { RequestGuestCount: true, GuestCount: null })
                    {
                        session = await Api.SetGuestCountAsync(session.Id, guestCount ?? 0);
                    }

                    order = await Api.CreateAndSubmitAsync(
                        restaurantId,
                        selectedTable!,
                        session!.Id,
                        serviceMode,
                        quantities,
                        notes
                    );
                }
                else
                {
                    order = takeawayRequiresPrepayment
                        ? await Api.CreatePayAndSubmitQuickSaleAsync(
                            restaurantId,
                            serviceMode,
                            customerName.Trim(),
                            quickPaymentMethod,
                            cashRegister?.Id,
                            quantities,
                            notes
                        )
                        : await Api.CreateAndSubmitTakeawayOnAccountAsync(
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
                hasSessionOrders = HasPhysicalLocation;
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

    private async Task CompletePickup(OrderResponse pickup)
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
        {
            return;
        }

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

    private async Task CancelSession()
    {
        if (session is null)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CancelSessionAsync(session.Id);
                success = true;
                message = "Apertura cancelada y ubicación liberada.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    private async Task OpenBill()
    {
        if (session is null)
        {
            return;
        }

        billOpen = true;
        await Run(LoadBill);
    }

    private async Task LoadBill()
    {
        if (session is null)
        {
            return;
        }

        bill = await Api.BillAsync(session.Id);
        OrderDetailResponse[] details = await Task.WhenAll(
            bill.Orders.Select(order => Api.OrderAsync(order.OrderId))
        );
        billOrders = details.ToDictionary(order => order.Id);
    }

    private static bool CanCancelLine(
        SessionBillOrderResponse billOrder,
        OrderDetailResponse order,
        OrderLineDetailResponse line
    )
    {
        return billOrder.PaymentStatus != "Paid"
            && line.Status == "Active"
            && OrderProgress.CanCancelBeforePreparation(order.Status)
            && order.Stations.Any(station =>
                station.Code == line.PreparationStationCode && station.Status == "Pending"
            );
    }

    private async Task CancelLine(SessionBillOrderResponse order, OrderLineDetailResponse line)
    {
        string? reason = await Js.InvokeAsync<string?>(
            "prompt",
            $"Motivo para cancelar {line.Quantity} × {line.ProductName}:"
        );
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        bool confirmed = await Js.InvokeAsync<bool>(
            "confirm",
            "La línea desaparecerá del KDS si cocina todavía no comenzó. ¿Continuar?"
        );
        if (!confirmed)
        {
            return;
        }

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

    private void CloseBill()
    {
        billOpen = false;
    }

    private async Task CheckoutSession()
    {
        if (session is null || busy)
        {
            return;
        }

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

    private async Task ReleaseSession()
    {
        if (session is null)
        {
            return;
        }

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

    private void BackToTables()
    {
        selectedTable = null;
        session = null;
        serviceMode = "DineIn";
        menu = [];
        quantities = [];
        notes = [];
        category = null;
        hasSessionOrders = false;
        bill = null;
        billOrders = [];
        billOpen = false;
        checkoutKey = Guid.Empty;
        paymentReference = string.Empty;
    }

    private async Task OpenCashRegisterPanel()
    {
        cashPanelOpen = true;
        await RefreshCashRegister();
        PrepareReconciliation();
    }

    private void CloseCashRegisterPanel()
    {
        cashPanelOpen = false;
    }

    private async Task RefreshCashRegister()
    {
        await Run(
            async () =>
            {
                cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
                PrepareReconciliation();
            },
            false
        );
    }

    private async Task OpenDailyCashRegister()
    {
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
    }

    private async Task CloseDailyCashRegister()
    {
        if (cashRegister is null)
        {
            return;
        }

        decimal reconciledTotal = reconciliationAmounts.Values.Sum();
        if (
            !await Js.InvokeAsync<bool>(
                "confirm",
                $"Total esperado: {cashRegister.ExpectedTotal:0.00} €. Total conciliado: {reconciledTotal:0.00} €. ¿Cerrar el turno?"
            )
        )
        {
            return;
        }

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

    private void PrepareReconciliation()
    {
        reconciliationAmounts =
            cashRegister?.ExpectedByMethod.ToDictionary(
                x => x.Method,
                x => x.Expected,
                StringComparer.OrdinalIgnoreCase
            ) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    private string FilterClass(string value)
    {
        return tableFilter == value ? "filter active" : "filter";
    }

    private string CategoryClass(string? value)
    {
        return category == value ? "filter active" : "filter";
    }

    private static string StatusLabel(string status)
    {
        return status == "Occupied" ? "Ocupada" : "Disponible";
    }

    private string TableOrderSummary(TableResponse table)
    {
        List<OrderResponse> orders = activeOrders.Where(x => x.TableId == table.Id).ToList();
        return OrderProgress.Summarize(orders.Select(x => x.Status));
    }

    private string SessionOrderSummary(Guid sessionId)
    {
        return OrderProgress.Summarize(
            activeOrders.Where(x => x.DiningSessionId == sessionId).Select(x => x.Status)
        );
    }

    private static string PaymentLabel(string method)
    {
        return method switch
        {
            "Cash" => "Efectivo",
            "Card" => "Tarjeta / datáfono",
            "Online" => "Pago online",
            "Cheque" => "Cheque",
            _ => method,
        };
    }

    private static string ModeLabel(string mode)
    {
        return mode switch
        {
            "Bar" => "BARRA",
            "Takeaway" => "PARA LLEVAR",
            _ => "SERVICIO EN MESA",
        };
    }

    private async Task Run(Func<Task> action, bool clearMessage = true)
    {
        if (clearMessage)
        {
            message = null;
        }

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

    private async Task ConnectRealtimeAsync()
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

    private Task HandleOrderUpdated(OrderRealtimeNotification notification)
    {
        return ScheduleRealtimeRefresh(notification.RestaurantId);
    }

    private async Task HandleTableChanged(DiningTableChangedNotification notification)
    {
        if (login is null || notification.RestaurantId != restaurantId)
        {
            return;
        }

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

    private Task ScheduleRealtimeRefresh(Guid changedRestaurantId)
    {
        if (login is null || changedRestaurantId != restaurantId)
        {
            return Task.CompletedTask;
        }

        realtimeRefresh?.Cancel();
        realtimeRefresh?.Dispose();
        realtimeRefresh = new CancellationTokenSource();
        _ = RefreshAfterOrderEventAsync(realtimeRefresh.Token);
        return Task.CompletedTask;
    }

    private async Task RefreshAfterOrderEventAsync(CancellationToken cancellationToken)
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

    private void StartReconciliation()
    {
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        reconciliation = new CancellationTokenSource();
        _ = ReconcileOperationalDataAsync(reconciliation.Token);
    }

    private async Task ReconcileOperationalDataAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (login is null || busy)
                    {
                        return;
                    }

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
