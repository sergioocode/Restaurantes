using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Pages;

public partial class Home
{
    private const string SessionKey = "restaurantes.backoffice.login";
    private string module = "menu";
    internal string search = "",
        allowedNetworksText = "";
    private string? message;
    internal bool busy;
    private bool ok;
    private bool initializing = true;
    private LoginResponse? login;
    private Guid restaurantId;
    internal List<CategoryResponse> categories = [];
    internal List<ProductResponse> products = [];
    private List<MenuItemResponse> menu = [];
    internal List<KitchenStationResponse> stations = [];
    private List<RestaurantResponse> restaurants = [];
    internal List<TableResponse> tables = [];
    internal DiningPolicyResponse policy = new();
    internal List<StaffUserResponse> users = [];
    internal Dictionary<Guid, MenuEditor> editors = [];
    internal CategoryDraft categoryDraft = new();
    internal ProductDraft productDraft = new();
    internal RestaurantDraft restaurantDraft = new();
    internal TableDraft tableDraft = new();
    internal List<ZoneResponse> zones = [];
    internal ZoneResponse zoneDraft = new();
    internal KitchenStationDraft stationDraft = new();
    internal StaffUserDraft userDraft = new();
    internal ProviderSettingsResponse? providerSettings;
    internal bool MicrosoftConfigured => providerSettings?.MicrosoftConfigured == true;
    internal bool GoogleConfigured => providerSettings?.GoogleConfigured == true;

    private static readonly string[] Roles =
    [
        "Admin",
        "Gerente",
        "Contabilidad",
        "Marketing",
        "Manager",
        "Camarero",
        "Kds",
    ];

    internal bool IsAdmin => login?.AllRestaurantsRoles?.Contains("Admin") == true;
    internal Guid? CurrentUserId => login?.User.Id;
    private bool HasBackofficeAccess =>
        login?.AllRestaurantsRoles?.Any(role => RoleHasPermission(role, "backoffice.access"))
            == true
        || login?.Restaurants.Any(x => x.Permissions.Contains("backoffice.access")) == true;
    internal bool CanManageUsers => HasGlobalPermission("identity.manage");
    internal bool CanAssignAllRestaurants =>
        login?.AllRestaurantsRoles?.Any(role => RoleHasPermission(role, "identity.manage")) == true;
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
    private List<RestaurantResponse> SelectableRestaurants =>
        login?.AllRestaurantsRoles?.Any(role => RoleHasPermission(role, "backoffice.access"))
        == true
            ? restaurants
            : restaurants
                .Where(x =>
                    login!.Restaurants.Any(a =>
                        a.RestaurantId == x.Id && a.Permissions.Contains("backoffice.access")
                    )
                )
                .ToList();
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
            "tables" => "OPERACIÓN DEL LOCAL",
            "restaurants" => "CADENA",
            "categories" or "products" => "CATÁLOGO GLOBAL",
            "users" => "SEGURIDAD",
            _ => "CATÁLOGO DEL LOCAL",
        };
    private string ModuleTitle =>
        module switch
        {
            "tables" => "Mesas, barra y reglas de venta",
            "restaurants" => "Locales",
            "stations" => "Estaciones Kitchen Display System",
            "categories" => "Categorías",
            "products" => "Productos",
            "users" => "Usuarios, roles y locales",
            _ => "Menú, precios y estaciones KDS",
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
            restaurantId =
                login
                    .Restaurants.FirstOrDefault(x => x.Permissions.Contains("backoffice.access"))
                    ?.RestaurantId
                ?? Guid.Empty;
            await LoadAllCore();
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
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        Api.AccessToken = null;
        login = null;
        AuthState.SetSession(null);
        message = null;
    }

    private async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out restaurantId))
        {
            return;
        }

        await LoadLocal();
    }

    private async Task LoadAll()
    {
        await Run(LoadAllCore);
    }

    private async Task LoadAllCore()
    {
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
        restaurants = await Api.RestaurantsAsync();
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

    private async Task LoadLocal()
    {
        await Run(LoadLocalCore);
    }

    private async Task LoadLocalCore()
    {
        if (SelectableRestaurants.Count == 0)
        {
            menu = [];
            stations = [];
            tables = [];
            zones = [];
            policy = new();
            return;
        }
        menu = HasLocalPermission("catalog.manage") ? await Api.MenuAsync(restaurantId) : [];
        stations = HasLocalPermission("kds.use") ? await Api.StationsAsync(restaurantId) : [];
        tables = HasLocalPermission("tables.read") ? await Api.TablesAsync(restaurantId) : [];
        zones = HasLocalPermission("tables.read") ? await Api.ZonesAsync(restaurantId) : [];
        tableDraft = new() { ZoneId = zones.FirstOrDefault()?.Id ?? Guid.Empty };
        zoneDraft = new();
        policy = HasLocalPermission("tables.read")
            ? await Api.DiningPolicyAsync(restaurantId)
            : new();
        allowedNetworksText = string.Join(Environment.NewLine, policy.QrAllowedNetworks);
        editors = products.ToDictionary(
            p => p.Id,
            p =>
            {
                MenuItemResponse? m = menu.FirstOrDefault(x => x.ProductId == p.Id);
                CategoryResponse? c = categories.FirstOrDefault(x => x.Id == p.CategoryId);
                return new MenuEditor
                {
                    Price = m?.Price ?? p.BasePrice,
                    IsAvailable = m?.IsAvailable ?? false,
                    PreparationStationCode =
                        m?.PreparationStationCode ?? c?.DefaultStationCode ?? "GENERAL",
                    PreparationStationName =
                        m?.PreparationStationName ?? c?.DefaultStationName ?? "General",
                };
            }
        );
    }

    internal async Task SaveMenuItem(ProductResponse p)
    {
        await Run(
            async () =>
            {
                MenuItemResponse accepted = await Api.ConfigureAsync(
                    restaurantId,
                    p.Id,
                    editors[p.Id]
                );
                MenuItemResponse saved = await ConfirmMenuProjection(accepted);
                menu.RemoveAll(x => x.ProductId == p.Id);
                menu.Add(saved);
                editors[p.Id].Price = saved.Price;
                editors[p.Id].IsAvailable = saved.IsAvailable;
                editors[p.Id].PreparationStationCode = saved.PreparationStationCode;
                editors[p.Id].PreparationStationName = saved.PreparationStationName;
                ok = true;
                message =
                    $"{p.Name} actualizado para {CurrentRestaurant?.Name} y confirmado en la vista de lectura.";
            },
            false
        );
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

    internal async Task SaveStation(KitchenStationResponse item)
    {
        await Run(
            async () =>
            {
                await Api.UpdateStationAsync(item);
                await RefreshStations();
                ok = true;
                message = "Estación KDS actualizada.";
            },
            false
        );
    }

    private async Task RefreshStations()
    {
        await Task.Delay(600);
        stations = await Api.StationsAsync(restaurantId);
        menu = await Api.MenuAsync(restaurantId);
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
                    "Categoría agregada. Crea sus productos y habilítalos en Menú por local para publicarlos.";
            },
            false
        );
    }

    internal async Task SaveCategory(CategoryResponse item)
    {
        await Run(
            async () =>
            {
                CategoryResponse accepted = await Api.UpdateCategoryAsync(item);
                await ConfirmCategoryProjection(accepted);
                await RefreshCatalogProjection();
                ok = true;
                message = "Categoría actualizada y confirmada en la vista de lectura.";
            },
            false
        );
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
                    "Producto agregado al catálogo global. Habilítalo y define precio/estación en Menú por local.";
            },
            false
        );
    }

    internal async Task SaveProduct(ProductResponse item)
    {
        await Run(
            async () =>
            {
                await Api.UpdateProductAsync(item);
                await RefreshCatalogProjection();
                ok = true;
                message = "Producto actualizado.";
            },
            false
        );
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
                await Api.EnsureDefaultStationsAsync(created.Id);
                restaurantDraft = new();
                await Task.Delay(800);
                restaurants = await Api.RestaurantsAsync();
                restaurantId = created.Id;
                await LoadLocalCore();
                module = "restaurants";
                ok = true;
                message =
                    "Local creado con las cuatro estaciones KDS predeterminadas. Asigna usuarios antes de operarlo.";
            },
            false
        );
    }

    internal async Task SaveRestaurant()
    {
        if (CurrentRestaurant is null)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.UpdateRestaurantAsync(CurrentRestaurant);
                await Task.Delay(600);
                restaurants = await Api.RestaurantsAsync();
                ok = true;
                message = "Local actualizado.";
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

    internal async Task DeleteZone(ZoneResponse zone)
    {
        await Run(
            async () =>
            {
                await Api.DeleteZoneAsync(restaurantId, zone.Id);
                await RefreshZones();
                ok = true;
                message = "Zona vacía eliminada.";
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

    internal async Task SaveTable(TableResponse item)
    {
        await Run(
            async () =>
            {
                await Api.UpdateTableAsync(item);
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message = "Ubicación actualizada.";
            },
            false
        );
    }

    internal async Task DeleteTable(TableResponse item)
    {
        await Run(
            async () =>
            {
                await Api.DeleteTableAsync(item.Id);
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message =
                    "Ubicación eliminada. Su historial se conserva y el código queda disponible.";
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

    internal async Task SavePolicy()
    {
        await Run(
            async () =>
            {
                policy.QrAllowedNetworks = allowedNetworksText.Split(
                    ['\r', '\n', ',', ';'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                policy = await Api.UpdateDiningPolicyAsync(restaurantId, policy);
                allowedNetworksText = string.Join(Environment.NewLine, policy.QrAllowedNetworks);
                ok = true;
                message = "Política del local actualizada.";
            },
            false
        );
    }

    private async Task LoadUsersCore()
    {
        users = await Api.UsersAsync();
    }

    internal async Task CreateUser()
    {
        await Run(
            async () =>
            {
                if (!CanAssignAllRestaurants && userDraft.RestaurantId is null)
                {
                    throw new InvalidOperationException("Selecciona un local.");
                }

                userDraft.AllRestaurants = userDraft.RestaurantId is null;
                await Api.CreateUserAsync(userDraft);
                userDraft = new();
                if (!CanAssignAllRestaurants)
                {
                    userDraft.RestaurantId = ManageableRestaurants.FirstOrDefault()?.Id;
                    userDraft.AllRestaurants = false;
                }
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
                if (!CanAssignAllRestaurants && user.RestaurantId is null)
                {
                    throw new InvalidOperationException("Selecciona un local.");
                }

                user.AllRestaurants = user.RestaurantId is null;
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
        return GlobalRoleHasPermission(permission)
            || login
                ?.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId)
                ?.Permissions.Contains(permission) == true;
    }

    internal bool HasGlobalPermission(string permission)
    {
        return GlobalRoleHasPermission(permission)
            || login?.Restaurants.Any(x => x.Permissions.Contains(permission)) == true;
    }

    private bool GlobalRoleHasPermission(string permission)
    {
        string? role = login?.AllRestaurantsRoles?.FirstOrDefault();
        return role is not null && RoleHasPermission(role, permission);
    }

    private static bool RoleHasPermission(string role, string permission)
    {
        return role switch
        {
            "Admin" => true,
            "Gerente" or "Contabilidad" => permission
                is "backoffice.access"
                    or "dashboard.read"
                    or "reports.financial.read"
                    or "payments.refund",
            "Marketing" => permission
                is "backoffice.access"
                    or "dashboard.read"
                    or "reports.financial.read"
                    or "reports.marketing.read",
            "Manager" => permission
                is "backoffice.access"
                    or "pos.use"
                    or "tables.read"
                    or "tables.manage"
                    or "tables.release"
                    or "orders.create"
                    or "orders.manage"
                    or "orders.recover"
                    or "payments.capture"
                    or "cash-register.manage"
                    or "catalog.manage"
                    or "kds.use"
                    or "reports.kitchen.read",
            "Camarero" => permission
                is "commander.use"
                    or "tables.read"
                    or "orders.create"
                    or "orders.manage",
            "Kds" => permission is "kds.use",
            _ => false,
        };
    }

    private string CurrentRoleFor(Guid id)
    {
        return login?.AllRestaurantsRoles?.FirstOrDefault()
            ?? login?.Restaurants.FirstOrDefault(x => x.RestaurantId == id)?.Role
            ?? "Sin asignar";
    }

    private string NavClass(string value)
    {
        return module == value ? "nav-active" : "";
    }

    internal string RestaurantName(Guid id)
    {
        return restaurants.FirstOrDefault(x => x.Id == id)?.Name ?? id.ToString();
    }

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

    internal static string StatusLabel(string status)
    {
        return status == "Occupied" ? "Ocupada" : "Disponible";
    }

    internal void SelectStation(MenuEditor editor, ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
        {
            return;
        }

        editor.PreparationStationCode = station.Code;
        editor.PreparationStationName = station.Name;
    }

    internal void SelectCategoryStation(CategoryResponse categoryItem, ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
        {
            return;
        }

        categoryItem.DefaultStationCode = station.Code;
        categoryItem.DefaultStationName = station.Name;
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

    private async Task<MenuItemResponse> ConfirmMenuProjection(MenuItemResponse expected)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            List<MenuItemResponse> projected = await Api.MenuAsync(expected.RestaurantId);
            MenuItemResponse? current = projected.FirstOrDefault(x =>
                x.ProductId == expected.ProductId && x.Version >= expected.Version
            );
            if (current is not null)
            {
                return current;
            }

            await Task.Delay(300);
        }
        throw new InvalidOperationException(
            "El cambio se guardó, pero la proyección Read del catálogo no lo confirmó. Comprueba Catalog Publisher, Consumer y RabbitMQ."
        );
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
