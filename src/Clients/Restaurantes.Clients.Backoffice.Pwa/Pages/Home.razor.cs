using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Pages;

public partial class Home
{
    internal static string StatusLabel(string status) =>
        status == "Occupied" ? "Ocupada" : "Disponible";

    private const string SessionKey = "restaurantes.backoffice.login";
    private const string OrdersReportReadPermission = "orders.report.read";
    private string module = "catalog-design";
    private string[] selectedNavigationNodes = [];
    private List<NavigationNode> navigationNodes = [];
    private static readonly Dictionary<string, object> SidebarAttributes = new()
    {
        ["class"] = "backoffice-sidebar",
    };
    internal string search = "";
    private string? message;
    internal bool busy;
    private bool ok;
    private bool initializing = true;
    private LoginResponse? login;
    internal Guid restaurantId;
    internal List<CategoryResponse> categories = [];
    internal List<ProductResponse> products = [];
    internal List<KitchenStationResponse> stations = [];
    internal List<ManagementOrderResponse> historyOrders = [];
    internal List<SaleResponse> historySales = [];
    internal DateOnly historyDate = DateOnly.FromDateTime(DateTime.Today);
    internal SaleResponse? selectedSale;
    internal List<RestaurantResponse> restaurants = [];
    internal List<CompanyResponse> companies = [];
    internal List<TableResponse> tables = [];
    internal List<StaffUserResponse> users = [];
    private List<StaffUserResponse> authorizedUsers = [];
    internal bool sidebarOpen = true;
    internal CategoryDraft categoryDraft = new();
    internal ProductDraft productDraft = new();
    internal RestaurantDraft restaurantDraft = new();
    internal CompanyDraft companyDraft = new();
    internal TableDraft tableDraft = new();
    internal List<ZoneResponse> zones = [];
    internal ZoneResponse zoneDraft = new();
    internal KitchenStationDraft stationDraft = new();
    internal StaffUserDraft userDraft = new();
    internal bool UserEmailAlreadyAuthorized =>
        !string.IsNullOrWhiteSpace(userDraft.Email)
        && authorizedUsers.Any(user =>
            string.Equals(
                user.Email.Trim(),
                userDraft.Email.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        );
    internal ProviderSettingsResponse? providerSettings;
    internal bool CanViewCompanySettings =>
        IsAdmin
        || login?.AllRestaurantsRoles?.Any(role => role is "Gerente" or "Contabilidad") == true
        || login?.Restaurants.Any(access => access.Role is "Gerente" or "Contabilidad") == true;
    internal bool CanManageCompanySettings => IsAdmin;
    internal bool CanManageRestaurantSettings => IsAdmin || HasLocalPermission("tables.manage");
    private CatalogDesignModule? catalogDesignModule;
    private TaxesModule? taxesModule;
    private AllergensModule? allergensModule;

    internal List<NavigationNode> NavigationNodes => navigationNodes;

    private List<NavigationNode> BuildNavigationNodes()
    {
        List<NavigationNode> nodes = [];
        List<NavigationNode> locationChildren = [];
        if (HasAnyLocalPermission("tables.read"))
        {
            locationChildren.Add(new("tables", "Mesas", "group-locations"));
            locationChildren.Add(new("zones", "Zonas", "group-locations"));
        }
        if (IsCurrentRestaurantManager)
        {
            locationChildren.Add(new("stations", "Estaciones KDS", "group-locations"));
        }
        AddGroup(nodes, "group-locations", "Ubicaciones", locationChildren);

        List<NavigationNode> productChildren = [];
        if (IsCurrentRestaurantManager)
        {
            productChildren.Add(new("catalog-design", "Carta del local", "group-products"));
        }
        if (IsAdmin)
        {
            productChildren.Add(new("products", "Carta Global", "group-products"));
            productChildren.Add(new("taxes", "Impuestos", "group-products"));
            productChildren.Add(new("allergens", "Alérgenos", "group-products"));
            productChildren.Add(new("categories", "Categorías", "group-products"));
        }
        AddGroup(nodes, "group-products", "Productos", productChildren);

        List<NavigationNode> reportChildren = [];
        if (HasAnyLocalPermission(OrdersReportReadPermission))
        {
            reportChildren.Add(new("daily-orders-report", "Pedidos por día", "group-reports"));
        }
        if (HasLocalPermission("reports.marketing.read"))
        {
            reportChildren.Add(new("kitchen-times-report", "Tiempos de cocina", "group-reports"));
        }
        AddGroup(nodes, "group-reports", "Informes", reportChildren);

        List<NavigationNode> configurationChildren = [];
        if (CanViewCompanySettings)
        {
            configurationChildren.Add(new("companies", "Empresas", "group-configuration"));
            configurationChildren.Add(new("restaurants", "Locales", "group-configuration"));
        }
        if (IsAdmin)
        {
            configurationChildren.Add(new("users", "Usuarios", "group-configuration"));
        }
        AddGroup(nodes, "group-configuration", "Configuración", configurationChildren);

        List<NavigationNode> managementChildren = [];
        if (HasLocalPermission("payments.refund"))
        {
            managementChildren.Add(new("refunds", "Devoluciones", "group-management"));
        }
        AddGroup(nodes, "group-management", "Gestión", managementChildren);

        return nodes;
    }

    internal bool MicrosoftConfigured => providerSettings?.MicrosoftConfigured == true;
    internal bool GoogleConfigured => providerSettings?.GoogleConfigured == true;

    private static readonly string[] Roles = ["Manager", "Camarero", "Kds"];

    internal bool IsAdmin => login?.AllRestaurantsRoles?.Contains("Admin") == true;
    internal bool IsCurrentRestaurantManager =>
        !IsAdmin
        && CurrentRoleFor(restaurantId) == "Manager"
        && HasLocalPermission("catalog.manage");
    internal Guid? CurrentUserId => login?.User.Id;
    private bool HasBackofficeAccess =>
        HasGlobalPermission("backoffice.access")
        || login?.Restaurants.Any(x => x.Permissions.Contains("backoffice.access")) == true;
    internal bool CanManageUsers => HasGlobalPermission("identity.manage");
    internal bool CanAssignAllRestaurants =>
        login?.AllRestaurantsPermissions?.Contains("identity.manage") == true;
    internal IEnumerable<string> AssignableRoles => Roles;
    internal List<RestaurantResponse> ManageableRestaurants =>
        restaurants
            .Where(x =>
                CanAssignAllRestaurants
                || login!.Restaurants.Any(a =>
                    a.RestaurantId == x.Id && a.Permissions.Contains("identity.manage")
                )
            )
            .ToList();
    internal List<RestaurantResponse> SelectableRestaurants =>
        login?.AllRestaurantsPermissions?.Contains("backoffice.access") == true
            ? restaurants
            : restaurants
                .Where(x =>
                    login!.Restaurants.Any(a =>
                        a.RestaurantId == x.Id && a.Permissions.Contains("backoffice.access")
                    )
                )
                .ToList();
    internal List<RestaurantResponse> VisibleCompanyRestaurants =>
        IsAdmin
        || login?.AllRestaurantsRoles?.Any(role => role is "Gerente" or "Contabilidad") == true
            ? restaurants
            : restaurants
                .Where(restaurant =>
                    login?.Restaurants.Any(access =>
                        access.RestaurantId == restaurant.Id
                        && access.Role is "Gerente" or "Contabilidad"
                    ) == true
                )
                .ToList();
    internal List<RestaurantResponse> OrderRestaurants =>
        restaurants
            .Where(x =>
                login?.AllRestaurantsPermissions?.Contains("orders.read") == true
                || login?.AllRestaurantsPermissions?.Contains("orders.manage") == true
                || login?.Restaurants.Any(a =>
                    a.RestaurantId == x.Id
                    && (
                        a.Permissions.Contains("orders.read")
                        || a.Permissions.Contains("orders.manage")
                    )
                ) == true
            )
            .ToList();
    internal List<RestaurantResponse> ReportRestaurants =>
        restaurants
            .Where(x =>
                login?.AllRestaurantsPermissions?.Contains(OrdersReportReadPermission) == true
                || login?.Restaurants.Any(a =>
                    a.RestaurantId == x.Id && a.Permissions.Contains(OrdersReportReadPermission)
                ) == true
            )
            .ToList();
    internal List<RestaurantResponse> ContextRestaurants =>
        module == "daily-orders-report" ? ReportRestaurants : SelectableRestaurants;
    internal RestaurantResponse? CurrentRestaurant =>
        restaurants.FirstOrDefault(x => x.Id == restaurantId);
    internal List<ProductResponse> VisibleProducts =>
        products
            .Where(x =>
                string.IsNullOrWhiteSpace(search)
                || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Sku.Contains(search, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(x => x.Name)
            .ToList();
    private string ModuleEyebrow =>
        module switch
        {
            "tables" or "zones" => "OPERACIÓN DEL LOCAL",
            "orders" => "OPERACIÓN DEL LOCAL",
            "daily-orders-report" => "INFORMES",
            "restaurants" => "CADENA",
            "companies" => "CADENA",
            "categories" or "products" or "taxes" or "allergens" => "CATÁLOGO GLOBAL",
            "catalog-design" => "CATÁLOGO DEL LOCAL",
            "users" => "SEGURIDAD",
            _ => "CATÁLOGO DEL LOCAL",
        };
    private string ModuleTitle =>
        module switch
        {
            "tables" => "Mesas",
            "zones" => "Zonas del restaurante",
            "restaurants" => "Locales",
            "companies" => "Empresas",
            "stations" => "Estaciones Kitchen Display System",
            "orders" => "Pedidos",
            "daily-orders-report" => "Pedidos por día",
            "categories" => "Categorías",
            "products" => "Carta Global",
            "taxes" => "Impuestos",
            "allergens" => "Alérgenos",
            "catalog-design" => "Carta del local",
            "users" => "Usuarios, roles y locales",
            _ => "Backoffice",
        };

    private bool HasMultiRestaurantReportAccess =>
        login?.AllRestaurantsRoles?.Any(role =>
            role is "Admin" or "Gerente" or "Contabilidad" or "Marketing"
        ) == true;
    private bool ShowRestaurantContextSelector =>
        (module == "daily-orders-report" && HasMultiRestaurantReportAccess)
        || (IsAdmin && module is ("users" or "categories" or "tables" or "zones"));

    private string? RefreshLabel =>
        module switch
        {
            "catalog-design" when IsCurrentRestaurantManager => "Recargar",
            "orders" when OrderRestaurants.Count > 0 => "Consultar pedidos",
            "daily-orders-report" when ReportRestaurants.Count > 0 => "Consultar pedidos",
            "stations" when IsCurrentRestaurantManager => "Actualizar estaciones",
            "tables" when HasLocalPermission("tables.manage") => "Actualizar mesas",
            "zones" when HasLocalPermission("tables.manage") => "Actualizar zonas",
            "restaurants" when CanViewCompanySettings => "Actualizar locales",
            "companies" when CanViewCompanySettings => "Actualizar empresas",
            "categories" when HasGlobalPermission("catalog.global.manage") =>
                "Actualizar categorías",
            "products" when HasGlobalPermission("catalog.global.manage") => "Recargar",
            "taxes" when HasGlobalPermission("catalog.global.manage") => "Actualizar impuestos",
            "allergens" when HasGlobalPermission("catalog.global.manage") => "Actualizar alérgenos",
            "users" when CanManageUsers => "Actualizar usuarios",
            _ => null,
        };

    internal void ToggleSidebar() => sidebarOpen = !sidebarOpen;

    private static void AddGroup(
        List<NavigationNode> nodes,
        string id,
        string text,
        List<NavigationNode> children
    )
    {
        if (children.Count == 0)
        {
            return;
        }
        nodes.Add(new(id, text, HasChildren: true, Expanded: true));
        nodes.AddRange(children);
    }

    internal void BeforeNavigationSelect(Syncfusion.Blazor.Navigations.NodeSelectEventArgs args)
    {
        if (args.NodeData.Id.StartsWith("group-", StringComparison.Ordinal))
        {
            args.Cancel = true;
        }
        else if (args.NodeData.Id is "kitchen-times-report" or "refunds")
        {
            args.Cancel = true;
        }
    }

    internal async Task OnNavigationSelected(Syncfusion.Blazor.Navigations.NodeSelectEventArgs args)
    {
        string selected = args.NodeData.Id;
        if (selected.StartsWith("group-", StringComparison.Ordinal))
        {
            return;
        }
        SetModule(selected);
        if (selected is "orders" or "daily-orders-report")
        {
            await RefreshOrders();
        }
    }

    private void SetModule(string value)
    {
        module = value;
        selectedNavigationNodes = [value];
    }

    private Task RefreshCurrentModule() =>
        module switch
        {
            "catalog-design" => catalogDesignModule?.Refresh() ?? Task.CompletedTask,
            "taxes" => taxesModule?.Refresh() ?? Task.CompletedTask,
            "allergens" => allergensModule?.Refresh() ?? Task.CompletedTask,
            _ => RefreshModule(),
        };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            providerSettings = await Api.ProviderAsync();
            string? code = QueryValue("login_code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                LoginResponse response = await Api.ExchangeAsync(code);
                await Js.InvokeVoidAsync("history.replaceState", null, "", Nav.BaseUri);
                await AcceptLogin(response);
                return;
            }
            if (QueryValue("login_error") is not null)
            {
                message = "La cuenta no está autorizada o el proveedor rechazó el acceso.";
            }

            string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
            login = JsonSerializer.Deserialize<LoginResponse>(json);
            if (login is null || login.ExpiresAtUtc <= DateTime.UtcNow || !HasBackofficeAccess)
            {
                await Logout();
                return;
            }
            Api.AccessToken = login.AccessToken;
            AuthState.SetSession(login);
            SetModule(IsAdmin ? "products" : "catalog-design");
            restaurantId =
                login
                    .Restaurants.FirstOrDefault(x => x.Permissions.Contains("backoffice.access"))
                    ?.RestaurantId
                ?? Guid.Empty;
            await LoadAllCore();
            navigationNodes = BuildNavigationNodes();
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

    private void StartLogin(string provider)
    {
        Nav.NavigateTo(
            $"/api/identity/auth/start/{provider}?returnPath=%2Fbackoffice%2F",
            forceLoad: true
        );
    }

    private async Task AcceptLogin(LoginResponse response)
    {
        login = response;
        if (!HasBackofficeAccess)
        {
            throw new InvalidOperationException("Este perfil no tiene acceso al Backoffice.");
        }

        Api.AccessToken = login.AccessToken;
        AuthState.SetSession(login);
        SetModule(IsAdmin ? "products" : "catalog-design");
        restaurantId =
            login
                .Restaurants.FirstOrDefault(x => x.Permissions.Contains("backoffice.access"))
                ?.RestaurantId
            ?? Guid.Empty;
        await Js.InvokeVoidAsync(
            "sessionStorage.setItem",
            SessionKey,
            JsonSerializer.Serialize(login)
        );
        await LoadAllCore();
        navigationNodes = BuildNavigationNodes();
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        Api.AccessToken = null;
        login = null;
        AuthState.SetSession(null);
        message = null;
    }

    internal async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out restaurantId))
        {
            return;
        }

        await Run(async () =>
        {
            await LoadLocalCore();
            if (module == "users" && CanManageUsers)
            {
                await LoadUsersCore();
            }
            navigationNodes = BuildNavigationNodes();
        });
        if (module is "orders" or "daily-orders-report")
        {
            await RefreshOrders();
        }
    }

    internal bool HasAnyLocalPermission(string permission)
    {
        return login?.AllRestaurantsPermissions?.Contains(permission) == true
            || SelectableRestaurants.Any(restaurant =>
                login
                    ?.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurant.Id)
                    ?.Permissions.Contains(permission) == true
            );
    }

    internal Task RefreshModule()
    {
        return module switch
        {
            "catalog-design" => Run(RefreshProductsCore),
            "stations" => Run(RefreshStationsCore),
            "orders" or "daily-orders-report" => RefreshOrders(),
            "tables" => Run(RefreshTablesCore),
            "zones" => Run(RefreshTablesCore),
            "categories" => Run(RefreshCategoriesCore),
            "products" => Run(RefreshProductsCore),
            "restaurants" => Run(RefreshRestaurantsCore),
            "companies" => Run(RefreshCompanySettingsCore),
            "users" => Run(LoadUsersCore),
            _ => Task.CompletedTask,
        };
    }

    internal async Task OpenOrders()
    {
        SetModule("orders");
        await RefreshOrders();
    }

    internal Task RefreshOrders()
    {
        return Run(async () =>
        {
            IEnumerable<RestaurantResponse> allowed = (
                module == "daily-orders-report" ? ReportRestaurants : OrderRestaurants
            ).Where(restaurant => restaurant.Id == restaurantId);
            List<ManagementOrderResponse> result = [];
            List<SaleResponse> sales = [];
            foreach (RestaurantResponse restaurant in allowed)
            {
                int offset = 0;
                while (true)
                {
                    List<ManagementOrderResponse> page = await Api.ManagementOrdersAsync(
                        restaurant.Id,
                        historyDate,
                        offset
                    );
                    result.AddRange(page);
                    if (page.Count < 100)
                    {
                        break;
                    }

                    offset += page.Count;
                }
                if (module == "daily-orders-report")
                {
                    int saleOffset = 0;
                    while (true)
                    {
                        List<SaleResponse> page = await Api.SalesAsync(
                            restaurant.Id,
                            historyDate,
                            saleOffset
                        );
                        sales.AddRange(page);
                        if (page.Count < 100)
                        {
                            break;
                        }

                        saleOffset += page.Count;
                    }
                }
            }
            historyOrders = result.OrderByDescending(order => order.CreatedAtUtc).ToList();
            historySales = sales;
        });
    }

    internal ManagementOrderResponse? SaleOrderContext(SaleResponse sale) =>
        historyOrders
            .Where(order => sale.OrderIds.Contains(order.Id))
            .OrderBy(order => order.CreatedAtUtc)
            .FirstOrDefault();

    internal Task ShowSaleTicket(SaleResponse sale)
    {
        return InvokeAsync(() =>
        {
            selectedSale = sale;
            StateHasChanged();
        });
    }

    internal void CloseSaleTicket()
    {
        selectedSale = null;
    }

    internal Task PrintSaleTicket()
    {
        return Js.InvokeVoidAsync("window.print").AsTask();
    }

    internal static string PaymentMethodLabel(string method)
    {
        return method switch
        {
            "Card" => "Tarjeta / datáfono",
            "Cash" => "Efectivo",
            "Transfer" => "Transferencia",
            "Online" => "Pago online",
            "Cheque" => "Cheque",
            _ => method,
        };
    }

    internal static string TicketFormatLabel(string format) =>
        format switch
        {
            "PLATO" => "Plato",
            "RACION" => "Ración",
            "MEDIA_RACION" => "Media ración",
            "TAPA" => "Tapa",
            "UNIDAD" => "Unidad",
            "TERCIO" => "Tercio",
            "VASO" => "Vaso",
            "COPA" => "Copa",
            "COMBO" => "Combo",
            _ => format,
        };

    private async Task RefreshStationsCore()
    {
        stations = await Api.StationsAsync(restaurantId);
    }

    private async Task RefreshTablesCore()
    {
        await RefreshZones();
    }

    private async Task RefreshCategoriesCore()
    {
        categories = await Api.CategoriesAsync();
        stations =
            SelectableRestaurants.Count > 0 && HasLocalPermission("kds.use")
                ? await Api.StationsAsync(restaurantId)
                : [];
    }

    private async Task RefreshProductsCore()
    {
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
    }

    private async Task RefreshRestaurantsCore()
    {
        restaurants = await Api.RestaurantsAsync();
        companies = await Api.CompaniesAsync();
    }

    private async Task RefreshCompanySettingsCore()
    {
        companies = await Api.CompaniesAsync();
    }

    private async Task LoadAllCore()
    {
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
        restaurants = await Api.RestaurantsAsync();
        companies = await Api.CompaniesAsync();
        if (!CanAssignAllRestaurants && userDraft.RestaurantId is null)
        {
            userDraft.RestaurantId = ManageableRestaurants.FirstOrDefault()?.Id;
            userDraft.AllRestaurants = false;
        }
        if (!restaurants.Any(x => x.Id == restaurantId) && SelectableRestaurants.Count > 0)
        {
            restaurantId = SelectableRestaurants[0].Id;
        }

        if (CanManageUsers)
        {
            await LoadUsersCore();
        }

        await LoadLocalCore();
    }

    private async Task LoadLocalCore()
    {
        if (SelectableRestaurants.Count == 0)
        {
            stations = [];
            tables = [];
            zones = [];
            return;
        }
        stations =
            HasLocalPermission("kds.use") || IsCurrentRestaurantManager
                ? await Api.StationsAsync(restaurantId)
                : [];
        tables = HasLocalPermission("tables.read") ? await Api.TablesAsync(restaurantId) : [];
        zones = HasLocalPermission("tables.read") ? await Api.ZonesAsync(restaurantId) : [];
        tableDraft = new() { ZoneId = zones.FirstOrDefault()?.Id ?? Guid.Empty };
        zoneDraft = new();
    }

    internal async Task CreateStation()
    {
        await Run(
            async () =>
            {
                await Api.CreateStationAsync(restaurantId, stationDraft);
                stationDraft = new();
                await RefreshStations();
                ok = true;
                message = "Estación KDS agregada.";
            },
            false
        );
    }

    internal async Task<bool> SaveStationName(KitchenStationResponse item, string name)
    {
        if (!IsCurrentRestaurantManager || busy)
        {
            return false;
        }

        await Run(
            async () =>
            {
                ApplyStation(item, await Api.SetStationNameAsync(item, name));
                ok = true;
                message = "Nombre KDS actualizado.";
            },
            false
        );
        return ok;
    }

    internal async Task<bool> SaveStationPriority(KitchenStationResponse item, int priority)
    {
        if (!IsCurrentRestaurantManager || busy || item.IsPrimary)
        {
            return false;
        }

        await Run(
            async () =>
            {
                ApplyStation(item, await Api.SetStationPriorityAsync(item, priority));
                ok = true;
                message = "Prioridad KDS actualizada.";
            },
            false
        );
        return ok;
    }

    internal async Task SetStationFlag(KitchenStationResponse item, string field, bool selected)
    {
        if (!IsCurrentRestaurantManager || busy)
        {
            return;
        }

        KitchenStationResponse previous = CopyStation(item);
        KitchenStationResponse update = CopyStation(item);
        switch (field)
        {
            case nameof(KitchenStationResponse.IsPrimary):
                update.IsPrimary = selected;
                update.Priority = selected ? 0 : Math.Max(1, update.Priority);
                if (selected)
                {
                    update.RequiresPrimaryDispatch = false;
                }
                break;
            case nameof(KitchenStationResponse.RequiresPrimaryDispatch):
                update.RequiresPrimaryDispatch = selected;
                break;
            case nameof(KitchenStationResponse.IsActive):
                update.IsActive = selected;
                break;
            default:
                return;
        }

        ApplyStation(item, update);
        busy = true;
        message = null;
        ok = false;
        await InvokeAsync(StateHasChanged);
        try
        {
            ApplyStation(item, await Api.SetStationFlagAsync(item, field, selected));
        }
        catch (Exception error)
        {
            ApplyStation(item, previous);
            message = error.Message;
        }
        finally
        {
            busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static KitchenStationResponse CopyStation(KitchenStationResponse source) =>
        new()
        {
            Id = source.Id,
            RestaurantId = source.RestaurantId,
            Code = source.Code,
            Name = source.Name,
            IsPrimary = source.IsPrimary,
            RequiresPrimaryDispatch = source.RequiresPrimaryDispatch,
            Priority = source.Priority,
            IsActive = source.IsActive,
            Version = source.Version,
            UpdatedAtUtc = source.UpdatedAtUtc,
        };

    private static void ApplyStation(KitchenStationResponse target, KitchenStationResponse source)
    {
        target.Name = source.Name;
        target.IsPrimary = source.IsPrimary;
        target.RequiresPrimaryDispatch = source.RequiresPrimaryDispatch;
        target.Priority = source.Priority;
        target.IsActive = source.IsActive;
        target.Version = source.Version;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
    }

    private async Task RefreshStations()
    {
        await Task.Delay(600);
        stations = await Api.StationsAsync(restaurantId);
    }

    internal async Task CreateCategory()
    {
        await Run(
            async () =>
            {
                if (!stations.Any(x => x.IsActive && x.Code == categoryDraft.DefaultStationCode))
                {
                    throw new InvalidOperationException(
                        "Selecciona una estación KDS activa del local."
                    );
                }

                await Api.CreateCategoryAsync(categoryDraft);
                categoryDraft = new();
                await RefreshCatalogProjection();
                ok = true;
                message =
                    "Categoría agregada. Crea productos y asígnales precios predeterminados para incluirlos en la carta.";
            },
            false
        );
    }

    internal async Task<bool> SaveCategory(CategoryResponse item)
    {
        if (busy)
        {
            return false;
        }

        await Run(
            async () =>
            {
                CategoryResponse accepted = await Api.UpdateCategoryAsync(item);
                await ConfirmCategoryProjection(accepted);
                await RefreshCatalogProjection();
                ok = true;
            },
            true
        );
        return ok;
    }

    internal async Task CreateProduct()
    {
        await Run(
            async () =>
            {
                if (productDraft.CategoryId == Guid.Empty)
                {
                    throw new InvalidOperationException("Selecciona una categoría.");
                }

                await Api.CreateProductAsync(productDraft);
                productDraft = new();
                await RefreshCatalogProjection();
                ok = true;
                message =
                    "Producto agregado. Asigna impuesto, alérgenos, extras y precios predeterminados en Productos.";
            },
            false
        );
    }

    internal async Task SaveProduct(ProductResponse item)
    {
        await Run(
            async () =>
            {
                ProductResponse saved = await Api.SetProductNameAsync(item.Id, item.Name);
                item.Name = saved.Name;
                item.Version = saved.Version;
                item.UpdatedAtUtc = saved.UpdatedAtUtc;
                ok = true;
                message = "Producto actualizado.";
            },
            false
        );
    }

    internal async Task SaveProductCategory(ProductResponse item, ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out Guid categoryId))
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        Guid previous = item.CategoryId;
        await Run(async () =>
        {
            item.CategoryId = categoryId;
            await InvokeAsync(StateHasChanged);
            try
            {
                ProductResponse saved = await Api.SetProductCategoryAsync(item.Id, categoryId);
                item.CategoryId = saved.CategoryId;
                item.Version = saved.Version;
                item.UpdatedAtUtc = saved.UpdatedAtUtc;
            }
            catch
            {
                item.CategoryId = previous;
                throw;
            }
        });
    }

    internal async Task SaveProductActive(ProductResponse item, ChangeEventArgs args)
    {
        bool selected = args.Value is bool value && value;
        bool previous = item.IsActive;
        await Run(async () =>
        {
            item.IsActive = selected;
            await InvokeAsync(StateHasChanged);
            try
            {
                ProductResponse saved = await Api.SetProductActiveAsync(item.Id, selected);
                item.IsActive = saved.IsActive;
                item.Version = saved.Version;
                item.UpdatedAtUtc = saved.UpdatedAtUtc;
            }
            catch
            {
                item.IsActive = previous;
                throw;
            }
        });
    }

    private async Task RefreshCatalogProjection()
    {
        await Task.Delay(800);
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
        await LoadLocalCore();
    }

    internal async Task CreateRestaurant()
    {
        await Run(
            async () =>
            {
                RestaurantResponse created = await Api.CreateRestaurantAsync(restaurantDraft);
                restaurantDraft = new();
                restaurants.Add(created);
                restaurantId = created.Id;
                await LoadLocalCore();
                SetModule("restaurants");
                ok = true;
                message =
                    "Local creado. Configura sus estaciones KDS y asigna usuarios antes de operarlo.";
            },
            false
        );
    }

    internal async Task SaveRestaurant(RestaurantResponse restaurant)
    {
        await Run(
            async () =>
            {
                RestaurantResponse updated = await Api.UpdateRestaurantAsync(restaurant);
                restaurant.Name = updated.Name;
                restaurant.Address = updated.Address;
                restaurant.Version = updated.Version;
                restaurant.UpdatedAtUtc = updated.UpdatedAtUtc;
                ok = true;
                message = "Local actualizado.";
            },
            false
        );
    }

    internal Task ChangeRestaurantCompany(RestaurantResponse restaurant, ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out Guid companyId))
        {
            return Task.CompletedTask;
        }

        Guid? previous = restaurant.CompanyId;
        restaurant.CompanyId = companyId;
        return SaveRestaurantSelectionAsync(
            async () =>
            {
                RestaurantResponse updated = await Api.SetRestaurantCompanyAsync(
                    restaurant.Id,
                    companyId
                );
                restaurant.CompanyId = updated.CompanyId;
                restaurant.Version = updated.Version;
                restaurant.UpdatedAtUtc = updated.UpdatedAtUtc;
                ok = true;
                message = "Empresa del local actualizada.";
            },
            () => restaurant.CompanyId = previous
        );
    }

    internal Task ChangeRestaurantActive(RestaurantResponse restaurant, ChangeEventArgs args)
    {
        if (!bool.TryParse(args.Value?.ToString(), out bool isActive))
        {
            return Task.CompletedTask;
        }

        bool previous = restaurant.IsActive;
        restaurant.IsActive = isActive;
        return SaveRestaurantSelectionAsync(
            async () =>
            {
                RestaurantResponse updated = await Api.SetRestaurantActiveAsync(
                    restaurant.Id,
                    isActive
                );
                restaurant.IsActive = updated.IsActive;
                restaurant.Version = updated.Version;
                restaurant.UpdatedAtUtc = updated.UpdatedAtUtc;
                ok = true;
                message = "Estado del local actualizado.";
            },
            () => restaurant.IsActive = previous
        );
    }

    private async Task SaveRestaurantSelectionAsync(Func<Task> save, Action rollback)
    {
        await Run(save, false);
        if (ok)
        {
            return;
        }

        rollback();
    }

    internal async Task CreateCompany()
    {
        await Run(
            async () =>
            {
                CompanyResponse created = await Api.CreateCompanyAsync(companyDraft);
                companyDraft = new();
                companies.Add(created);
                companies = companies.OrderBy(item => item.LegalName).ToList();
                ok = true;
                message = "Empresa agregada.";
            },
            false
        );
    }

    internal async Task SaveCompany(CompanyResponse company)
    {
        await Run(
            async () =>
            {
                CompanyResponse updated = await Api.UpdateCompanyAsync(company);
                company.LegalName = updated.LegalName;
                company.TaxId = updated.TaxId;
                company.FiscalAddress = updated.FiscalAddress;
                company.Phone = updated.Phone;
                company.Email = updated.Email;
                company.Version = updated.Version;
                company.UpdatedAtUtc = updated.UpdatedAtUtc;
                ok = true;
                message = "Empresa actualizada.";
            },
            false
        );
    }

    private async Task RefreshZones()
    {
        zones = await Api.ZonesAsync(restaurantId);
        tables = await Api.TablesAsync(restaurantId);
        if (!zones.Any(x => x.Id == tableDraft.ZoneId))
        {
            tableDraft.ZoneId = zones.FirstOrDefault()?.Id ?? Guid.Empty;
        }
    }

    internal async Task CreateZone()
    {
        await Run(
            async () =>
            {
                await Api.SaveZoneAsync(restaurantId, zoneDraft);
                zoneDraft = new();
                await RefreshZones();
                ok = true;
                message = "Zona agregada.";
            },
            false
        );
    }

    internal async Task SaveZone(ZoneResponse zone)
    {
        await Run(
            async () =>
            {
                await Api.SaveZoneAsync(restaurantId, zone);
                await RefreshZones();
                ok = true;
                message = "Zona actualizada.";
            },
            false
        );
    }

    internal async Task CreateTable()
    {
        await Run(
            async () =>
            {
                await Api.CreateTableAsync(restaurantId, tableDraft);
                tableDraft = new() { ZoneId = tableDraft.ZoneId };
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message = "Ubicación agregada.";
            },
            false
        );
    }

    internal Task SaveTableLabel(TableResponse item, string label) =>
        UpdateTable(item, label, item.ZoneId, item.IsActive);

    internal Task SaveTableZone(TableResponse item, Guid zoneId) =>
        UpdateTable(item, item.Label, zoneId, item.IsActive);

    internal Task<bool> SetTableActive(TableResponse item, bool isActive) =>
        UpdateTable(item, item.Label, item.ZoneId, isActive);

    private async Task<bool> UpdateTable(
        TableResponse item,
        string label,
        Guid zoneId,
        bool isActive
    )
    {
        bool saved = false;
        await Run(
            async () =>
            {
                await Api.UpdateTableAsync(
                    new TableResponse
                    {
                        Id = item.Id,
                        Label = label,
                        ZoneId = zoneId,
                        IsActive = isActive,
                    }
                );
                tables = await Api.TablesAsync(restaurantId);
                saved = true;
            },
            true
        );
        return saved;
    }

    internal async Task DeleteTable(TableResponse item)
    {
        await Run(
            async () =>
            {
                await Api.DeleteTableAsync(item.Id);
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message = "Mesa eliminada.";
            },
            false
        );
    }

    internal async Task RotateQr(TableResponse item)
    {
        await Run(
            async () =>
            {
                bool confirmed = await Js.InvokeAsync<bool>(
                    "confirm",
                    $"El QR impreso de {item.Label} dejará de funcionar. ¿Regenerarlo?"
                );
                if (!confirmed)
                {
                    return;
                }

                await Api.RotateQrAsync(item.Id);
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message = "QR regenerado. Debes volver a imprimirlo.";
            },
            false
        );
    }

    private async Task LoadUsersCore()
    {
        List<StaffUserResponse> authorizedUsers = await Api.UsersAsync();
        this.authorizedUsers = authorizedUsers;
        users = authorizedUsers.Where(user => user.RestaurantId == restaurantId).ToList();
    }

    internal async Task CreateUser()
    {
        await Run(
            async () =>
            {
                if (restaurantId == Guid.Empty)
                {
                    throw new InvalidOperationException("Selecciona un local.");
                }

                if (UserEmailAlreadyAuthorized)
                {
                    throw new InvalidOperationException(
                        "Este correo ya está autorizado en un local y no puede asignarse a otro."
                    );
                }

                userDraft.RestaurantId = restaurantId;
                userDraft.AllRestaurants = false;
                await Api.CreateUserAsync(userDraft);
                userDraft = new() { RestaurantId = restaurantId, AllRestaurants = false };
                await LoadUsersCore();
                ok = true;
                message = "Cuenta autorizada.";
            },
            false
        );
    }

    internal async Task SaveUser(StaffUserResponse user)
    {
        await Run(
            async () =>
            {
                if (restaurantId == Guid.Empty)
                {
                    throw new InvalidOperationException("Selecciona un local.");
                }

                user.RestaurantId = restaurantId;
                user.AllRestaurants = false;
                await Api.UpdateUserAsync(user);
                await LoadUsersCore();
                ok = true;
                message = "Cuenta actualizada.";
            },
            false
        );
    }

    internal async Task DeleteUser(StaffUserResponse user)
    {
        bool confirmed = await Js.InvokeAsync<bool>(
            "confirm",
            $"¿Eliminar la cuenta {user.DisplayName} ({user.Email})?"
        );
        if (!confirmed)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.DeleteUserAsync(user.Id);
                await LoadUsersCore();
                ok = true;
                message = "Cuenta eliminada.";
            },
            false
        );
    }

    internal bool HasLocalPermission(string permission)
    {
        return login?.AllRestaurantsPermissions?.Contains(permission) == true
            || login
                ?.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId)
                ?.Permissions.Contains(permission) == true;
    }

    internal bool HasGlobalPermission(string permission)
    {
        return login?.AllRestaurantsPermissions?.Contains(permission) == true
            || login?.Restaurants.Any(x => x.Permissions.Contains(permission)) == true;
    }

    internal string CurrentRoleFor(Guid id)
    {
        return login?.AllRestaurantsRoles?.FirstOrDefault()
            ?? login?.Restaurants.FirstOrDefault(x => x.RestaurantId == id)?.Role
            ?? "Sin asignar";
    }

    internal sealed record NavigationNode(
        string Id,
        string Text,
        string? ParentId = null,
        bool HasChildren = false,
        bool Expanded = false
    );

    internal string RestaurantName(Guid id)
    {
        return restaurants.FirstOrDefault(x => x.Id == id)?.Name ?? id.ToString();
    }

    internal RestaurantResponse? RestaurantFor(Guid id) =>
        restaurants.FirstOrDefault(restaurant => restaurant.Id == id);

    internal string RestaurantNameWithId(Guid id)
    {
        return $"{RestaurantName(id)} · {id}";
    }

    internal static string DateValue(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd") ?? string.Empty;
    }

    internal static DateTime? ParseDate(object? value)
    {
        return DateTime.TryParse(value?.ToString(), out DateTime date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    internal static string RoleLabel(string role)
    {
        return role switch
        {
            "Admin" => "Administrador",
            "Gerente" => "Gerencia",
            "Contabilidad" => "Contabilidad",
            "Marketing" => "Marketing",
            "Manager" => "Manager Local",
            "Camarero" => "Camarero",
            "Kds" => "Cocina",
            _ => role,
        };
    }

    internal void SelectDraftCategoryStation(ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
        {
            return;
        }

        categoryDraft.DefaultStationCode = station.Code;
        categoryDraft.DefaultStationName = station.Name;
    }

    private async Task ConfirmCategoryProjection(CategoryResponse expected)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            CategoryResponse? current = (await Api.CategoriesAsync()).FirstOrDefault(x =>
                x.Id == expected.Id && x.Version >= expected.Version
            );
            if (current is not null)
            {
                return;
            }

            await Task.Delay(300);
        }
        throw new InvalidOperationException(
            "La categoría se guardó, pero la proyección Read del catálogo no la confirmó. Comprueba Catalog Publisher, Consumer y RabbitMQ."
        );
    }

    private async Task Run(Func<Task> action, bool clear = true)
    {
        if (clear)
        {
            message = null;
        }

        ok = false;
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
            await InvokeAsync(StateHasChanged);
        }
    }
}
