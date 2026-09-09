using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Pages;

public partial class Home
{
    const string SessionKey = "restaurantes.backoffice.login";
    string username = "admin",
        password = "Admin-2026!",
        search = "",
        module = "menu",
        allowedNetworksText = "";
    string? message;
    bool busy,
        ok;
    LoginResponse? login;
    Guid restaurantId;
    List<CategoryResponse> categories = [];
    List<ProductResponse> products = [];
    List<MenuItemResponse> menu = [];
    List<KitchenStationResponse> stations = [];
    List<RestaurantResponse> restaurants = [];
    List<TableResponse> tables = [];
    DiningPolicyResponse policy = new();
    List<StaffUserResponse> users = [];
    Dictionary<Guid, UserAssignmentDraft> assignmentDrafts = [];
    Dictionary<Guid, MenuEditor> editors = [];
    CategoryDraft categoryDraft = new();
    ProductDraft productDraft = new();
    RestaurantDraft restaurantDraft = new();
    TableDraft tableDraft = new();
    List<ZoneResponse> zones = [];
    ZoneResponse zoneDraft = new();
    KitchenStationDraft stationDraft = new();
    StaffUserDraft userDraft = new();

    static readonly string[] LocalRoles = ["Manager", "PosComandero", "Kds"];
    static readonly string[] GlobalRoles = ["Admin", "Gerente", "Contabilidad", "Oficina"];

    bool IsAdmin =>
        login?.GlobalRoles?.Contains("Admin") == true
        || login?.Restaurants.Any(x => x.Role == "Admin") == true;
    bool CanManageUsers => IsAdmin;
    IEnumerable<string> AssignableRoles => [.. GlobalRoles, .. LocalRoles];
    List<RestaurantResponse> ManageableRestaurants =>
        restaurants
            .Where(x =>
                IsAdmin
                || login!.Restaurants.Any(a =>
                    a.RestaurantId == x.Id && a.Permissions.Contains("identity.manage")
                )
            )
            .ToList();
    List<RestaurantResponse> SelectableRestaurants =>
        IsAdmin
            ? restaurants
            : restaurants
                .Where(x =>
                    login!.Restaurants.Any(a =>
                        a.RestaurantId == x.Id && a.Permissions.Contains("backoffice.access")
                    )
                )
                .ToList();
    RestaurantResponse? CurrentRestaurant => restaurants.FirstOrDefault(x => x.Id == restaurantId);
    List<ProductResponse> VisibleProducts =>
        products
            .Where(x =>
                string.IsNullOrWhiteSpace(search)
                || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Sku.Contains(search, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(x => x.Name)
            .ToList();
    string ModuleEyebrow =>
        module switch
        {
            "tables" => "OPERACIÓN DEL LOCAL",
            "restaurants" => "CADENA",
            "categories" or "products" => "CATÁLOGO GLOBAL",
            "users" => "SEGURIDAD",
            _ => "CATÁLOGO DEL LOCAL",
        };
    string ModuleTitle =>
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
        string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            login = JsonSerializer.Deserialize<LoginResponse>(json);
            if (
                login is null
                || login.ExpiresAtUtc <= DateTime.UtcNow
                || !login.Restaurants.Any(x => x.Permissions.Contains("backoffice.access"))
            )
            {
                await Logout();
                return;
            }
            Api.AccessToken = login.AccessToken;
            restaurantId = login
                .Restaurants.First(x => x.Permissions.Contains("backoffice.access"))
                .RestaurantId;
            await LoadAllCore();
        }
        catch
        {
            await Logout();
        }
    }

    async Task Login() =>
        await Run(async () =>
        {
            login = await Api.LoginAsync(username, password);
            if (!login.Restaurants.Any(x => x.Permissions.Contains("backoffice.access")))
                throw new InvalidOperationException("Este perfil no tiene acceso al Backoffice.");
            Api.AccessToken = login.AccessToken;
            restaurantId = login
                .Restaurants.First(x => x.Permissions.Contains("backoffice.access"))
                .RestaurantId;
            await Js.InvokeVoidAsync(
                "sessionStorage.setItem",
                SessionKey,
                JsonSerializer.Serialize(login)
            );
            await LoadAllCore();
        });

    async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        Api.AccessToken = null;
        login = null;
        message = null;
    }

    async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out restaurantId))
            return;
        await LoadLocal();
    }

    async Task LoadAll() => await Run(LoadAllCore);

    async Task LoadAllCore()
    {
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
        restaurants = await Api.RestaurantsAsync();
        if (!restaurants.Any(x => x.Id == restaurantId) && SelectableRestaurants.Count > 0)
            restaurantId = SelectableRestaurants[0].Id;
        if (CanManageUsers)
            await LoadUsersCore();
        await LoadLocalCore();
    }

    async Task LoadLocal() => await Run(LoadLocalCore);

    async Task LoadLocalCore()
    {
        menu = await Api.MenuAsync(restaurantId);
        stations = await Api.StationsAsync(restaurantId);
        tables = await Api.TablesAsync(restaurantId);
        zones = await Api.ZonesAsync(restaurantId);
        tableDraft = new() { ZoneId = zones.FirstOrDefault()?.Id ?? Guid.Empty };
        zoneDraft = new();
        policy = await Api.DiningPolicyAsync(restaurantId);
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

    async Task SaveMenuItem(ProductResponse p) =>
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

    async Task CreateStation() =>
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

    async Task SaveStation(KitchenStationResponse item) =>
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

    async Task RefreshStations()
    {
        await Task.Delay(600);
        stations = await Api.StationsAsync(restaurantId);
        menu = await Api.MenuAsync(restaurantId);
    }

    async Task CreateCategory() =>
        await Run(
            async () =>
            {
                if (!stations.Any(x => x.IsActive && x.Code == categoryDraft.DefaultStationCode))
                    throw new InvalidOperationException(
                        "Selecciona una estación KDS activa del local."
                    );
                await Api.CreateCategoryAsync(categoryDraft);
                categoryDraft = new();
                await RefreshCatalogProjection();
                ok = true;
                message =
                    "Categoría agregada. Crea sus productos y habilítalos en Menú por local para publicarlos.";
            },
            false
        );

    async Task SaveCategory(CategoryResponse item) =>
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

    async Task CreateProduct() =>
        await Run(
            async () =>
            {
                if (productDraft.CategoryId == Guid.Empty)
                    throw new InvalidOperationException("Selecciona una categoría.");
                await Api.CreateProductAsync(productDraft);
                productDraft = new();
                await RefreshCatalogProjection();
                ok = true;
                message =
                    "Producto agregado al catálogo global. Habilítalo y define precio/estación en Menú por local.";
            },
            false
        );

    async Task SaveProduct(ProductResponse item) =>
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

    async Task RefreshCatalogProjection()
    {
        await Task.Delay(800);
        categories = await Api.CategoriesAsync();
        products = await Api.ProductsAsync();
        await LoadLocalCore();
    }

    async Task CreateRestaurant() =>
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

    async Task SaveRestaurant()
    {
        if (CurrentRestaurant is null)
            return;
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

    async Task RefreshZones()
    {
        zones = await Api.ZonesAsync(restaurantId);
        tables = await Api.TablesAsync(restaurantId);
        if (!zones.Any(x => x.Id == tableDraft.ZoneId))
            tableDraft.ZoneId = zones.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    async Task CreateZone() =>
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

    async Task SaveZone(ZoneResponse zone) =>
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

    async Task DeleteZone(ZoneResponse zone) =>
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

    async Task CreateTable() =>
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

    async Task SaveTable(TableResponse item) =>
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

    async Task DeleteTable(TableResponse item) =>
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

    async Task RotateQr(TableResponse item) =>
        await Run(
            async () =>
            {
                bool confirmed = await Js.InvokeAsync<bool>(
                    "confirm",
                    $"El QR impreso de {item.Label} dejará de funcionar. ¿Regenerarlo?"
                );
                if (!confirmed)
                    return;
                await Api.RotateQrAsync(item.Id);
                tables = await Api.TablesAsync(restaurantId);
                ok = true;
                message = "QR regenerado. Debes volver a imprimirlo.";
            },
            false
        );

    async Task SavePolicy() =>
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

    async Task LoadUsersCore()
    {
        users = await Api.UsersAsync();
        assignmentDrafts = users.ToDictionary(x => x.Id, _ => new UserAssignmentDraft());
    }

    async Task CreateUser() =>
        await Run(
            async () =>
            {
                if (userDraft.RestaurantId == Guid.Empty)
                    throw new InvalidOperationException("Selecciona el local inicial.");
                await Api.CreateUserAsync(userDraft);
                userDraft = new();
                await LoadUsersCore();
                ok = true;
                message = "Usuario creado y asignado al local.";
            },
            false
        );

    async Task SaveAssignment(
        StaffUserResponse user,
        UserRestaurantAssignmentResponse assignment
    ) =>
        await Run(
            async () =>
            {
                await Api.SaveUserAssignmentAsync(user.Id, assignment);
                await LoadUsersCore();
                ok = true;
                message =
                    "Asignación actualizada. El usuario deberá iniciar sesión otra vez para usar los nuevos permisos.";
            },
            false
        );

    async Task AddAssignment(StaffUserResponse user) =>
        await Run(
            async () =>
            {
                UserAssignmentDraft draft = assignmentDrafts[user.Id];
                if (draft.RestaurantId == Guid.Empty)
                    throw new InvalidOperationException(
                        "Selecciona un local que todavía no tenga asignado."
                    );
                await Api.AddUserAssignmentAsync(user.Id, draft);
                await LoadUsersCore();
                ok = true;
                message = "Local asignado al usuario.";
            },
            false
        );

    bool HasLocalPermission(string permission) =>
        IsAdmin
        || login
            ?.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId)
            ?.Permissions.Contains(permission) == true;

    bool HasGlobalPermission(string permission) =>
        IsAdmin || login?.Restaurants.Any(x => x.Permissions.Contains(permission)) == true;

    string CurrentRoleFor(Guid id) =>
        IsAdmin
            ? "Admin"
            : login?.Restaurants.FirstOrDefault(x => x.RestaurantId == id)?.Role ?? "Sin asignar";

    string NavClass(string value) => module == value ? "nav-active" : "";

    IEnumerable<string> AssignableRolesFor(string current) =>
        AssignableRoles.Contains(current) ? AssignableRoles : AssignableRoles.Append(current);

    IEnumerable<RestaurantResponse> AvailableRestaurantsFor(StaffUserResponse user) =>
        ManageableRestaurants.Where(x => user.Restaurants.All(a => a.RestaurantId != x.Id));

    string RestaurantName(Guid id) =>
        restaurants.FirstOrDefault(x => x.Id == id)?.Name ?? id.ToString();

    string RestaurantNameWithId(Guid id) => $"{RestaurantName(id)} · {id}";

    static string DateValue(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;

    static DateTime? ParseDate(object? value) =>
        DateTime.TryParse(value?.ToString(), out DateTime date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;

    static string RoleLabel(string role) =>
        role switch
        {
            "Admin" => "Administrador",
            "Gerente" => "Gerencia",
            "Contabilidad" => "Contabilidad",
            "Oficina" => "Oficina",
            "Manager" => "Encargado de local",
            "PosComandero" => "Caja / Camarero",
            "Kds" => "Cocina",
            _ => role,
        };

    static string StatusLabel(string status) => status == "Occupied" ? "Ocupada" : "Disponible";

    void SelectStation(MenuEditor editor, ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
            return;
        editor.PreparationStationCode = station.Code;
        editor.PreparationStationName = station.Name;
    }

    void SelectCategoryStation(CategoryResponse categoryItem, ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
            return;
        categoryItem.DefaultStationCode = station.Code;
        categoryItem.DefaultStationName = station.Name;
    }

    void SelectDraftCategoryStation(ChangeEventArgs args)
    {
        KitchenStationResponse? station = stations.FirstOrDefault(x =>
            x.Code == args.Value?.ToString()
        );
        if (station is null)
            return;
        categoryDraft.DefaultStationCode = station.Code;
        categoryDraft.DefaultStationName = station.Name;
    }

    async Task<MenuItemResponse> ConfirmMenuProjection(MenuItemResponse expected)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            List<MenuItemResponse> projected = await Api.MenuAsync(expected.RestaurantId);
            MenuItemResponse? current = projected.FirstOrDefault(x =>
                x.ProductId == expected.ProductId && x.Version >= expected.Version
            );
            if (current is not null)
                return current;
            await Task.Delay(300);
        }
        throw new InvalidOperationException(
            "El cambio se guardó, pero la proyección Read del catálogo no lo confirmó. Comprueba Catalog Publisher, Consumer y RabbitMQ."
        );
    }

    async Task ConfirmCategoryProjection(CategoryResponse expected)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            CategoryResponse? current = (await Api.CategoriesAsync()).FirstOrDefault(x =>
                x.Id == expected.Id && x.Version >= expected.Version
            );
            if (current is not null)
                return;
            await Task.Delay(300);
        }
        throw new InvalidOperationException(
            "La categoría se guardó, pero la proyección Read del catálogo no la confirmó. Comprueba Catalog Publisher, Consumer y RabbitMQ."
        );
    }

    async Task Run(Func<Task> action, bool clear = true)
    {
        if (clear)
            message = null;
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
        }
    }
}
