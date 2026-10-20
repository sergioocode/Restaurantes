using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Syncfusion.Blazor;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeStationFlowTests : BunitContext
{
    [Theory]
    [InlineData("Admin", "Administrador")]
    [InlineData("Gerente", "Gerencia")]
    [InlineData("Contabilidad", "Contabilidad")]
    [InlineData("Marketing", "Marketing")]
    [InlineData("Manager", "Manager Local")]
    [InlineData("Camarero", "Camarero")]
    [InlineData("Kds", "Cocina")]
    [InlineData("Otro", "Otro")]
    public void Role_label_matches_each_supported_role(string role, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("RoleLabel", role));
    }

    [Theory]
    [InlineData("PLATO", "Plato")]
    [InlineData("RACION", "Ración")]
    [InlineData("MEDIA_RACION", "Media ración")]
    [InlineData("TAPA", "Tapa")]
    [InlineData("UNIDAD", "Unidad")]
    [InlineData("TERCIO", "Tercio")]
    [InlineData("VASO", "Vaso")]
    [InlineData("COPA", "Copa")]
    [InlineData("COMBO", "Combo")]
    [InlineData("Otro", "Otro")]
    public void Ticket_format_label_matches_each_supported_format(string format, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("TicketFormatLabel", format));
    }

    [Theory]
    [InlineData("Card", "Tarjeta / datáfono")]
    [InlineData("Cash", "Efectivo")]
    [InlineData("Transfer", "Transferencia")]
    [InlineData("Online", "Pago online")]
    [InlineData("Cheque", "Cheque")]
    [InlineData("Otro", "Otro")]
    public void Payment_method_label_matches_each_supported_method(string method, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("PaymentMethodLabel", method));
    }

    [Fact]
    public void Date_values_format_and_parse_with_utc_kind()
    {
        DateTime date = new(2026, 10, 20, 12, 30, 0);

        Assert.Equal("2026-10-20", InvokeStatic<string>("DateValue", date));
        Assert.Equal(string.Empty, InvokeStatic<string>("DateValue", [null]));
        Assert.Equal(
            DateTime.SpecifyKind(new DateTime(2026, 10, 20, 12, 30, 0), DateTimeKind.Utc),
            InvokeStatic<DateTime?>("ParseDate", "2026-10-20T12:30:00")
        );
        Assert.Null(InvokeStatic<DateTime?>("ParseDate", "not-a-date"));
        Assert.Null(InvokeStatic<DateTime?>("ParseDate", [null]));
    }

    [Fact]
    public async Task Creating_a_station_refreshes_the_list_and_category_selection_uses_known_stations()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse existing = Station(restaurantId);
        StationHandler handler = new(existing);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, existing);
        SetField(
            cut.Instance,
            "stationDraft",
            new KitchenStationDraft
            {
                Code = "BAR",
                Name = "Barra",
                Priority = 3,
                PrintTicket = true,
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateStation"));

        KitchenStationResponse created = Assert.Single(handler.Stations, x => x.Code == "BAR");
        Assert.Equal("Barra", created.Name);
        Assert.Equal(3, created.Priority);
        Assert.Equal(
            string.Empty,
            GetField<KitchenStationDraft>(cut.Instance, "stationDraft").Code
        );
        Assert.Contains("Estación KDS agregada", GetField<string>(cut.Instance, "message"));

        SetField(cut.Instance, "categoryDraft", new CategoryDraft());
        InvokeVoid(
            cut.Instance,
            "SelectDraftCategoryStation",
            new ChangeEventArgs { Value = "GRILL" }
        );
        CategoryDraft draft = GetField<CategoryDraft>(cut.Instance, "categoryDraft");
        Assert.Equal(existing.Code, draft.DefaultStationCode);
        Assert.Equal(existing.Name, draft.DefaultStationName);
        InvokeVoid(
            cut.Instance,
            "SelectDraftCategoryStation",
            new ChangeEventArgs { Value = "UNKNOWN" }
        );
        Assert.Equal(existing.Code, draft.DefaultStationCode);
    }

    [Fact]
    public async Task Changing_restaurant_loads_its_local_data_and_ignores_invalid_ids()
    {
        Guid firstRestaurantId = Guid.NewGuid();
        Guid secondRestaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(firstRestaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(firstRestaurantId, handler, station);
        List<string> permissions =
        [
            "backoffice.access",
            "catalog.manage",
            "kds.use",
            "tables.read",
        ];
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "manager", "Manager"),
                [
                    new RestaurantAccess(firstRestaurantId, "Manager", permissions),
                    new RestaurantAccess(secondRestaurantId, "Manager", permissions),
                ]
            )
        );
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = firstRestaurantId,
                    Name = "Centro",
                    IsActive = true,
                },
                new()
                {
                    Id = secondRestaurantId,
                    Name = "Norte",
                    IsActive = true,
                },
            }
        );
        SetField(cut.Instance, "module", "stations");

        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "RestaurantChanged",
                new ChangeEventArgs { Value = secondRestaurantId.ToString() }
            )
        );
        int requestsAfterChange = handler.Paths.Count;
        await cut.InvokeAsync(() =>
            Invoke(cut.Instance, "RestaurantChanged", new ChangeEventArgs { Value = "invalid" })
        );

        Assert.Equal(secondRestaurantId, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Contains(
            handler.Paths,
            path => path == $"/api/catalog/restaurants/{secondRestaurantId}/stations"
        );
        Assert.Equal(requestsAfterChange, handler.Paths.Count);
    }

    private static T InvokeStatic<T>(string methodName, params object?[] arguments) =>
        (T)
            typeof(BackofficeHome)
                .GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                )!
                .Invoke(null, arguments)!;

    [Fact]
    public void Operational_modules_show_no_access_without_their_permissions()
    {
        JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(null);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSyncfusionBlazor();
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        IRenderedComponent<BackofficeHome> cut = RenderHome(
            restaurantId,
            new StationHandler(station),
            station
        );
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "viewer", "Viewer"),
                []
            )
        );

        foreach (
            string module in new[]
            {
                "orders",
                "daily-orders-report",
                "cash-registers-report",
                "stations",
            }
        )
        {
            SetField(cut.Instance, "module", module);
            cut.Render();
            Assert.Contains("Sin acceso de edición", cut.Markup);
        }
    }

    [Fact]
    public void A_saved_manager_session_restores_its_restaurant_and_local_catalog()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSyncfusionBlazor();
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        LoginResponse login = new(
            "token",
            DateTime.UtcNow.AddHours(1),
            new LoginUser(Guid.NewGuid(), "manager", "Manager"),
            [
                new RestaurantAccess(
                    restaurantId,
                    "Manager",
                    ["backoffice.access", "catalog.manage", "kds.use", "tables.read"]
                ),
            ]
        );
        JSInterop
            .Setup<string?>("sessionStorage.getItem", _ => true)
            .SetResult(JsonSerializer.Serialize(login));
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(new SessionAuthenticationStateProvider());

        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Equal(restaurantId, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Equal("catalog-design", GetField<string>(cut.Instance, "module"));
        Assert.Equal(
            station.Id,
            Assert.Single(GetField<List<KitchenStationResponse>>(cut.Instance, "stations")).Id
        );
        Assert.Contains("Carta del local", cut.Markup);
        Assert.Contains(
            handler.Paths,
            path => path.EndsWith("/stations", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_valid_login_code_exchanges_the_session_and_loads_the_allowed_restaurant()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSyncfusionBlazor();
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        LoginResponse login = new(
            "exchanged-token",
            DateTime.UtcNow.AddHours(1),
            new LoginUser(Guid.NewGuid(), "manager", "Manager"),
            [
                new RestaurantAccess(
                    restaurantId,
                    "Manager",
                    ["backoffice.access", "catalog.manage", "kds.use"]
                ),
            ]
        );
        StationHandler handler = new(station) { ExchangeLogin = login };
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(new SessionAuthenticationStateProvider());
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.BaseUri + "#login_code=one-time-code");

        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Equal(restaurantId, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Equal("exchanged-token", GetField<LoginResponse>(cut.Instance, "login").AccessToken);
        Assert.Contains(handler.Paths, path => path == "/api/identity/auth/exchange");
    }

    [Fact]
    public async Task Station_flags_apply_server_response_and_rollback_rejected_changes()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId, version: 1);
        StationHandler handler = new(Station(restaurantId, station.Id, version: 1));
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);

        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SetStationFlag",
                station,
                nameof(KitchenStationResponse.PrintTicket),
                true
            )
        );
        Assert.True(station.PrintTicket);
        Assert.Equal(2, station.Version);
        Assert.Contains(
            handler.Paths,
            path => path.EndsWith("/print-ticket", StringComparison.Ordinal)
        );

        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SetStationFlag",
                station,
                nameof(KitchenStationResponse.IsPrimary),
                true
            )
        );
        Assert.True(station.IsPrimary);
        Assert.False(station.RequiresPrimaryDispatch);
        Assert.Equal(0, station.Priority);
        Assert.Contains(handler.Paths, path => path.EndsWith("/primary", StringComparison.Ordinal));

        handler.FailNextUpdate = true;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SetStationFlag",
                station,
                nameof(KitchenStationResponse.IsActive),
                false
            )
        );
        Assert.True(station.IsActive);
        Assert.Equal(3, station.Version);
        Assert.Contains(handler.Paths, path => path.EndsWith("/active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Station_refresh_preserves_newer_local_version_when_projection_lags()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse current = Station(restaurantId, version: 3);
        KitchenStationResponse stale = Station(restaurantId, current.Id, version: 2);
        StationHandler handler = new(stale);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, current);

        await cut.InvokeAsync(async () =>
        {
            MethodInfo refresh = typeof(BackofficeHome).GetMethod(
                "RefreshStationsCore",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)refresh.Invoke(cut.Instance, null)!;
        });

        KitchenStationResponse saved = Assert.Single(
            GetField<List<KitchenStationResponse>>(cut.Instance, "stations")
        );
        Assert.Same(current, saved);
        Assert.Equal(3, saved.Version);
        Assert.Equal(8, handler.StationReads);
        Assert.Contains(
            "se conserva la versión más reciente",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Station_name_and_priority_follow_server_values_and_primary_priority_is_locked()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId, version: 1);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);

        bool nameSaved = await cut.InvokeAsync(async () =>
            await (Task<bool>)Invoke(cut.Instance, "SaveStationName", station, "Parrilla nueva")
        );
        bool prioritySaved = await cut.InvokeAsync(async () =>
            await (Task<bool>)Invoke(cut.Instance, "SaveStationPriority", station, 4)
        );

        Assert.True(nameSaved);
        Assert.True(prioritySaved);
        Assert.Equal("Parrilla nueva", station.Name);
        Assert.Equal(4, station.Priority);
        Assert.Equal(3, station.Version);
        Assert.Contains(handler.Paths, path => path.EndsWith("/name", StringComparison.Ordinal));
        Assert.Contains(
            handler.Paths,
            path => path.EndsWith("/priority", StringComparison.Ordinal)
        );

        station.IsPrimary = true;
        int requestsBeforeLockedChange = handler.Paths.Count;
        bool primaryPrioritySaved = await cut.InvokeAsync(async () =>
            await (Task<bool>)Invoke(cut.Instance, "SaveStationPriority", station, 6)
        );

        Assert.False(primaryPrioritySaved);
        Assert.Equal(requestsBeforeLockedChange, handler.Paths.Count);
    }

    [Fact]
    public async Task Category_creation_requires_an_active_station_and_removes_stale_station_for_non_kds_category()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "categoryDraft",
            new CategoryDraft
            {
                Code = "FOOD",
                Name = "Comida",
                RequiresKds = true,
                DefaultStationCode = "MISSING",
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateCategory"));
        Assert.Equal(0, handler.CategoriesCreated);
        Assert.Contains("estación KDS activa", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "categoryDraft",
            new CategoryDraft
            {
                Code = "OTHER",
                Name = "Otros",
                RequiresKds = false,
                DefaultStationCode = "OLD",
                DefaultStationName = "Antigua",
            }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateCategory"));

        Assert.Equal(1, handler.CategoriesCreated);
        Assert.Equal(
            string.Empty,
            handler.CreatedCategory!.Value.GetProperty("defaultStationCode").GetString()
        );
        Assert.Equal(
            string.Empty,
            handler.CreatedCategory.Value.GetProperty("defaultStationName").GetString()
        );
        Assert.Contains("Categoría agregada", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "categoryDraft",
            new CategoryDraft
            {
                Code = "GRILL",
                Name = "Parrilla",
                RequiresKds = true,
                DefaultStationCode = station.Code,
                DefaultStationName = station.Name,
            }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateCategory"));

        Assert.Equal(2, handler.CategoriesCreated);
        Assert.Equal(
            station.Code,
            handler.CreatedCategory!.Value.GetProperty("defaultStationCode").GetString()
        );
        Assert.Equal(
            station.Name,
            handler.CreatedCategory.Value.GetProperty("defaultStationName").GetString()
        );
    }

    [Fact]
    public async Task Creating_and_saving_zones_refreshes_the_restaurant_projection()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "zoneDraft",
            new ZoneResponse { Name = "Terraza", RequestGuestCount = true }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateZone"));

        ZoneResponse created = Assert.Single(handler.Zones);
        Assert.Equal("Terraza", created.Name);
        Assert.True(created.RequestGuestCount);
        Assert.Contains("Zona agregada", GetField<string>(cut.Instance, "message"));

        created.Name = "Terraza cubierta";
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveZone", created));

        Assert.Equal(
            "Terraza cubierta",
            Assert.Single(GetField<List<ZoneResponse>>(cut.Instance, "zones")).Name
        );
        Assert.Contains("Zona actualizada", GetField<string>(cut.Instance, "message"));
        Assert.Contains(handler.Paths, path => path.EndsWith("/zones", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, path => path.Contains("/zones/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Company_creation_and_update_keep_the_sorted_server_values()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "companyDraft",
            new CompanyDraft
            {
                LegalName = "Restaurantes Z",
                TaxId = "B12345678",
                FiscalAddress = "Calle Mayor 1",
                Phone = "600000000",
                Email = "contacto@example.com",
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateCompany"));

        CompanyResponse created = Assert.Single(
            GetField<List<CompanyResponse>>(cut.Instance, "companies")
        );
        Assert.Equal("Restaurantes Z", created.LegalName);
        Assert.Equal(string.Empty, GetField<CompanyDraft>(cut.Instance, "companyDraft").LegalName);

        created.LegalName = "Restaurantes A";
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveCompany", created));

        Assert.Equal(
            "Restaurantes A",
            Assert.Single(GetField<List<CompanyResponse>>(cut.Instance, "companies")).LegalName
        );
        Assert.Equal(2, created.Version);
        Assert.Contains("Empresa actualizada", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task Creating_a_restaurant_selects_it_and_clears_the_creation_form()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "restaurantDraft",
            new RestaurantDraft
            {
                Code = "NORTE",
                Name = "Norte",
                Address = "Calle Norte 1",
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateRestaurant"));

        RestaurantResponse created = Assert.Single(
            GetField<List<RestaurantResponse>>(cut.Instance, "restaurants"),
            restaurant => restaurant.Code == "NORTE"
        );
        Assert.Equal(created.Id, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Equal("Norte", created.Name);
        Assert.Equal(string.Empty, GetField<RestaurantDraft>(cut.Instance, "restaurantDraft").Name);
        Assert.Contains("Local creado", GetField<string>(cut.Instance, "message"));
        Assert.Contains("/api/restaurant-operations/restaurants", handler.Paths);
    }

    [Fact]
    public async Task Saving_a_category_waits_for_the_updated_read_projection()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        CategoryResponse category = new()
        {
            Id = Guid.NewGuid(),
            Code = "FOOD",
            Name = "Comida preparada",
            DefaultStationCode = station.Code,
            DefaultStationName = station.Name,
            RequiresKds = true,
            IsActive = true,
            Version = 1,
        };
        StationHandler handler = new(station);
        handler.Categories.Add(category);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveCategory", category));

        Assert.True(GetField<bool>(cut.Instance, "ok"));
        Assert.Equal(2, category.Version);
        Assert.Equal("Comida preparada", Assert.Single(handler.Categories).Name);
        Assert.Contains(handler.Paths, path => path == $"/api/catalog/categories/{category.Id}");
        Assert.True(handler.CategoryProjectionReads >= 2);
    }

    [Fact]
    public async Task Categories_module_renders_and_saves_names_stations_and_flags()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse grill = Station(restaurantId);
        KitchenStationResponse oven = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = "OVEN",
            Name = "Horno",
            IsActive = true,
            Version = 1,
        };
        CategoryResponse category = new()
        {
            Id = Guid.NewGuid(),
            Code = "FOOD",
            Name = "Comida",
            DefaultStationCode = grill.Code,
            DefaultStationName = grill.Name,
            RequiresKds = true,
            IsActive = true,
            Version = 1,
        };
        StationHandler handler = new(grill);
        handler.Stations.Add(oven);
        handler.Categories.Add(category);
        IRenderedComponent<BackofficeHome> home = RenderHome(restaurantId, handler, grill);
        SetField(
            home.Instance,
            "login",
            GetField<LoginResponse>(home.Instance, "login") with
            {
                AllRestaurantsPermissions = ["catalog.global.manage"],
            }
        );
        SetField(home.Instance, "categories", new List<CategoryResponse> { category });

        IRenderedComponent<CategoriesModule> module = Render<CategoriesModule>(parameters =>
            parameters.Add(component => component.Host, home.Instance)
        );

        Assert.Contains("Nueva categoría", module.Markup);
        Assert.Contains("Comida", module.Markup);
        Assert.Contains("Estación KDS", module.Markup);

        module.Find(".category-name-editor input").Change("Comida nueva");
        await module.Find(".category-name-editor button").ClickAsync();
        Assert.Equal("Comida nueva", category.Name);

        CategoryResponse projected = Assert.Single(
            GetField<List<CategoryResponse>>(home.Instance, "categories")
        );
        await module.InvokeAsync(() =>
            Invoke(module.Instance, "SetStation", projected, new ChangeEventArgs { Value = "OVEN" })
        );
        Assert.Equal("OVEN", projected.DefaultStationCode);
        Assert.Equal("Horno", projected.DefaultStationName);

        await module.FindAll(".category-row .category-visibility input")[0].ChangeAsync(false);
        Assert.False(
            Assert.Single(GetField<List<CategoryResponse>>(home.Instance, "categories")).ShowInPos
        );
        await module.FindAll(".category-row .category-visibility input")[1].ChangeAsync(false);
        Assert.False(
            Assert
                .Single(GetField<List<CategoryResponse>>(home.Instance, "categories"))
                .ShowInCommander
        );
        await module.FindAll(".category-row .category-visibility input")[2].ChangeAsync(false);
        Assert.False(
            Assert
                .Single(GetField<List<CategoryResponse>>(home.Instance, "categories"))
                .ShowInCustomerQr
        );
    }

    [Fact]
    public void Categories_module_shows_access_message_without_global_catalog_permission()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> home = RenderHome(restaurantId, handler, station);

        IRenderedComponent<CategoriesModule> module = Render<CategoriesModule>(parameters =>
            parameters.Add(component => component.Host, home.Instance)
        );

        Assert.Contains("Sin acceso de edición", module.Markup);
        Assert.DoesNotContain("Nueva categoría", module.Markup);
    }

    [Fact]
    public async Task Restaurant_edits_apply_server_values_and_rejected_selection_rolls_back()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        RestaurantResponse restaurant = Assert.Single(
            GetField<List<RestaurantResponse>>(cut.Instance, "restaurants")
        );
        restaurant.Name = "Centro actualizado";
        restaurant.Address = "Calle nueva";

        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveRestaurant", restaurant));

        Assert.Equal("Centro actualizado", restaurant.Name);
        Assert.Equal("Calle nueva", restaurant.Address);
        Assert.Contains("Local actualizado", GetField<string>(cut.Instance, "message"));

        Guid companyId = Guid.NewGuid();
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "ChangeRestaurantCompany",
                restaurant,
                new ChangeEventArgs { Value = companyId.ToString() }
            )
        );
        Assert.Equal(companyId, restaurant.CompanyId);

        handler.RejectRestaurantSelections = true;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "ChangeRestaurantActive",
                restaurant,
                new ChangeEventArgs { Value = false }
            )
        );
        Assert.True(restaurant.IsActive);

        Guid? savedCompanyId = restaurant.CompanyId;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "ChangeRestaurantCompany",
                restaurant,
                new ChangeEventArgs { Value = "invalid" }
            )
        );
        Assert.Equal(savedCompanyId, restaurant.CompanyId);
    }

    [Fact]
    public async Task User_creation_scopes_account_to_selected_restaurant_and_rejects_duplicate_email()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "userDraft",
            new StaffUserDraft
            {
                Email = "  cajero@example.com ",
                DisplayName = "Cajero",
                Role = "Camarero",
                AllRestaurants = true,
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateUser"));

        Assert.Equal(1, handler.UserCreations);
        StaffUserResponse created = Assert.Single(
            GetField<List<StaffUserResponse>>(cut.Instance, "users")
        );
        Assert.Equal(restaurantId, created.RestaurantId);
        Assert.False(created.AllRestaurants);
        Assert.Contains("Cuenta autorizada", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "userDraft",
            new StaffUserDraft { Email = "CAJERO@example.com", DisplayName = "Otro" }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateUser"));

        Assert.Equal(1, handler.UserCreations);
        Assert.Contains("ya está autorizado", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task User_updates_are_scoped_and_deletion_requires_confirmation()
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        ConfirmingJsRuntime js = new(true);
        Services.AddSingleton<IJSRuntime>(js);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "userDraft",
            new StaffUserDraft
            {
                Email = "camarero@example.com",
                DisplayName = "Camarero inicial",
                Role = "Camarero",
                AllRestaurants = true,
            }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateUser"));
        StaffUserResponse user = Assert.Single(
            GetField<List<StaffUserResponse>>(cut.Instance, "users")
        );

        user.DisplayName = "Camarero actualizado";
        user.Role = "Manager";
        user.RestaurantId = null;
        user.AllRestaurants = true;
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveUser", user));

        StaffUserResponse saved = Assert.Single(
            GetField<List<StaffUserResponse>>(cut.Instance, "users")
        );
        Assert.Equal("Camarero actualizado", saved.DisplayName);
        Assert.Equal("Manager", saved.Role);
        Assert.Equal(restaurantId, saved.RestaurantId);
        Assert.False(saved.AllRestaurants);
        Assert.Contains("Cuenta actualizada", GetField<string>(cut.Instance, "message"));

        await cut.InvokeAsync(() => Invoke(cut.Instance, "DeleteUser", saved));

        Assert.Equal(1, js.Confirmations);
        Assert.Empty(GetField<List<StaffUserResponse>>(cut.Instance, "users"));
        Assert.Contains("Cuenta eliminada", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task Table_creation_update_and_deletion_refresh_the_location_projection()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(
            cut.Instance,
            "tableDraft",
            new TableDraft
            {
                ZoneId = zoneId,
                Code = "T1",
                Label = "Terraza 1",
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateTable"));

        TableResponse table = Assert.Single(handler.Tables);
        Assert.Equal("Terraza 1", table.Label);
        Assert.Contains("Ubicación agregada", GetField<string>(cut.Instance, "message"));
        bool updated = false;
        await cut.InvokeAsync(async () =>
        {
            updated = await (Task<bool>)
                typeof(BackofficeHome)
                    .GetMethod(
                        "SetTableActive",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                    )!
                    .Invoke(cut.Instance, [table, false])!;
        });

        Assert.True(updated);
        Assert.False(table.IsActive);
        Assert.Contains(handler.Paths, path => path == $"/api/dining/tables/{table.Id}");

        await cut.InvokeAsync(() => Invoke(cut.Instance, "DeleteTable", table));

        Assert.Empty(handler.Tables);
        Assert.Contains("Mesa eliminada", GetField<string>(cut.Instance, "message"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotating_a_table_qr_requires_confirmation_and_refreshes_the_printed_code(
        bool confirmed
    )
    {
        Guid restaurantId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        TableResponse table = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ZoneId = Guid.NewGuid(),
            Code = "T1",
            Label = "Terraza 1",
            QrCode = "old-code",
            IsActive = true,
        };
        handler.Tables.Add(table);
        ConfirmingJsRuntime js = new(confirmed);
        Services.AddSingleton<IJSRuntime>(js);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "RotateQr", table));

        Assert.Equal(1, js.Confirmations);
        Assert.Equal(confirmed, handler.Paths.Contains($"/api/dining/tables/{table.Id}/qr/rotate"));
        if (confirmed)
        {
            Assert.Equal(
                "new-code",
                Assert.Single(GetField<List<TableResponse>>(cut.Instance, "tables")).QrCode
            );
            Assert.Contains("Debes volver a imprimirlo", GetField<string>(cut.Instance, "message"));
        }
        else
        {
            Assert.Equal("old-code", table.QrCode);
        }
    }

    [Fact]
    public async Task Product_creation_requires_category_and_product_edits_follow_server_values()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        KitchenStationResponse station = Station(restaurantId);
        StationHandler handler = new(station);
        IRenderedComponent<BackofficeHome> cut = RenderHome(restaurantId, handler, station);
        SetField(cut.Instance, "productDraft", new ProductDraft { Sku = "COF", Name = "Café" });

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateProduct"));

        Assert.Empty(handler.Products);
        Assert.Contains("Selecciona una categoría", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "productDraft",
            new ProductDraft
            {
                Sku = "COF",
                Name = "Café",
                CategoryId = categoryId,
            }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateProduct"));

        ProductResponse product = Assert.Single(handler.Products);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.Contains("Producto agregado", GetField<string>(cut.Instance, "message"));

        product.Name = "Café actualizado";
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SaveProduct", product));
        Assert.Equal("Café actualizado", product.Name);
        Assert.Contains("Producto actualizado", GetField<string>(cut.Instance, "message"));

        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SaveProductCategory",
                product,
                new ChangeEventArgs { Value = Guid.NewGuid().ToString() }
            )
        );
        Assert.NotEqual(categoryId, product.CategoryId);

        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SaveProductActive",
                product,
                new ChangeEventArgs { Value = false }
            )
        );
        Assert.False(product.IsActive);

        Guid selectedCategory = product.CategoryId;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SaveProductCategory",
                product,
                new ChangeEventArgs { Value = "not-a-guid" }
            )
        );
        Assert.Equal(selectedCategory, product.CategoryId);

        handler.RejectProductUpdates = true;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SaveProductCategory",
                product,
                new ChangeEventArgs { Value = Guid.NewGuid().ToString() }
            )
        );
        Assert.Equal(selectedCategory, product.CategoryId);

        product.IsActive = true;
        await cut.InvokeAsync(() =>
            Invoke(
                cut.Instance,
                "SaveProductActive",
                product,
                new ChangeEventArgs { Value = false }
            )
        );
        Assert.True(product.IsActive);
    }

    private IRenderedComponent<BackofficeHome> RenderHome(
        Guid restaurantId,
        StationHandler handler,
        KitchenStationResponse station
    )
    {
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(new SessionAuthenticationStateProvider());
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "manager", "Manager"),
                [
                    new RestaurantAccess(
                        restaurantId,
                        "Manager",
                        ["backoffice.access", "catalog.manage", "kds.use"]
                    ),
                ]
            )
        );
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new()
                {
                    Id = restaurantId,
                    Name = "Centro",
                    IsActive = true,
                },
            }
        );
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(cut.Instance, "stations", new List<KitchenStationResponse> { station });
        return cut;
    }

    private static KitchenStationResponse Station(
        Guid restaurantId,
        Guid? id = null,
        int version = 1
    ) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = "GRILL",
            Name = "Parrilla",
            IsActive = true,
            RequiresPrimaryDispatch = true,
            Priority = 2,
            Version = version,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static Task Invoke(BackofficeHome home, string name, params object?[] args) =>
        (Task)
            typeof(BackofficeHome)
                .GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )!
                .Invoke(home, args)!;

    private static Task Invoke(CategoriesModule component, string name, params object?[] args) =>
        (Task)
            component
                .GetType()
                .GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )!
                .Invoke(component, args)!;

    private static void InvokeVoid(BackofficeHome home, string name, params object?[] args) =>
        typeof(BackofficeHome)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(home, args);

    private static T GetField<T>(object component, string name) =>
        (T)
            component
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(component)!;

    private static void SetField(object component, string name, object value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private sealed class StationHandler(KitchenStationResponse station) : HttpMessageHandler
    {
        public List<KitchenStationResponse> Stations { get; } = [station];
        public bool FailNextUpdate { get; set; }
        public List<string> Paths { get; } = [];
        public int StationReads { get; private set; }
        public int CategoriesCreated { get; private set; }
        public int CategoryProjectionReads { get; private set; }
        public JsonElement? CreatedCategory { get; private set; }
        public int UserCreations { get; private set; }
        private readonly List<StaffUserResponse> users = [];
        public List<TableResponse> Tables { get; } = [];
        public List<ZoneResponse> Zones { get; } = [];
        public List<CompanyResponse> Companies { get; } = [];
        public List<CategoryResponse> Categories { get; } = [];
        public RestaurantResponse Restaurant { get; } =
            new()
            {
                Id = station.RestaurantId,
                Name = "Centro",
                IsActive = true,
            };
        public RestaurantResponse? CreatedRestaurant { get; private set; }
        public List<ProductResponse> Products { get; } = [];
        public bool RejectProductUpdates { get; set; }
        public bool RejectRestaurantSelections { get; set; }
        public LoginResponse? ExchangeLogin { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (request.Method == HttpMethod.Post && path == "/api/identity/auth/exchange")
                return ExchangeLogin is null
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : Json(ExchangeLogin);
            if (
                request.Method == HttpMethod.Get
                && path == "/api/restaurant-operations/restaurants"
            )
                return Json(
                    CreatedRestaurant is null
                        ? new[] { Restaurant }
                        : new[] { Restaurant, CreatedRestaurant }
                );
            if (
                request.Method == HttpMethod.Post
                && path == "/api/restaurant-operations/restaurants"
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CreatedRestaurant = new RestaurantResponse
                {
                    Id = Guid.NewGuid(),
                    Code = body.RootElement.GetProperty("code").GetString()!,
                    Name = body.RootElement.GetProperty("name").GetString()!,
                    Address = body.RootElement.GetProperty("address").GetString()!,
                    IsActive = true,
                    Version = 1,
                };
                return Json(CreatedRestaurant);
            }
            if (request.Method == HttpMethod.Get && path == "/api/restaurant-operations/companies")
                return Json(Companies);
            if (path == "/api/restaurant-operations/companies" && request.Method == HttpMethod.Post)
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CompanyResponse created = JsonSerializer.Deserialize<CompanyResponse>(
                    body.RootElement.GetRawText(),
                    JsonSerializerOptions.Web
                )!;
                created.Id = Guid.NewGuid();
                created.Version = 1;
                created.UpdatedAtUtc = DateTime.UtcNow;
                Companies.Add(created);
                return Json(created);
            }
            if (
                path.StartsWith("/api/restaurant-operations/companies/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put
            )
            {
                Guid companyId = Guid.Parse(path.Split('/')[4]);
                CompanyResponse? company = Companies.FirstOrDefault(item => item.Id == companyId);
                if (company is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                JsonElement values = body.RootElement;
                company.LegalName = values.GetProperty("legalName").GetString()!;
                company.TaxId = values.GetProperty("taxId").GetString()!;
                company.FiscalAddress = values.GetProperty("fiscalAddress").GetString()!;
                company.Phone = values.GetProperty("phone").GetString();
                company.Email = values.GetProperty("email").GetString();
                company.Version++;
                company.UpdatedAtUtc = DateTime.UtcNow;
                return Json(company);
            }
            if (
                path.StartsWith("/api/restaurant-operations/restaurants/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put
            )
            {
                string[] segments = path.Split('/');
                if (RejectRestaurantSelections && segments.Length > 5)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                if (segments.Length == 5)
                {
                    Restaurant.Name = body.RootElement.GetProperty("name").GetString()!;
                    Restaurant.Address = body.RootElement.GetProperty("address").GetString()!;
                }
                else if (segments[5] == "company")
                {
                    Restaurant.CompanyId = body.RootElement.GetProperty("companyId").GetGuid();
                }
                else if (segments[5] == "active")
                {
                    Restaurant.IsActive = body.RootElement.GetProperty("isActive").GetBoolean();
                }
                Restaurant.Version++;
                Restaurant.UpdatedAtUtc = DateTime.UtcNow;
                return Json(Restaurant);
            }
            if (path == "/api/identity/users" && request.Method == HttpMethod.Get)
                return Json(users);
            if (path == "/api/identity/users" && request.Method == HttpMethod.Post)
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                JsonElement user = body.RootElement;
                users.Add(
                    new StaffUserResponse
                    {
                        Id = Guid.NewGuid(),
                        Email = user.GetProperty("email").GetString()!,
                        DisplayName = user.GetProperty("displayName").GetString()!,
                        Role = user.GetProperty("role").GetString()!,
                        RestaurantId = user.GetProperty("restaurantId").GetGuid(),
                        AllRestaurants = user.GetProperty("allRestaurants").GetBoolean(),
                        IsActive = true,
                    }
                );
                UserCreations++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (
                path.StartsWith("/api/identity/users/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put
            )
            {
                Guid userId = Guid.Parse(path.Split('/')[4]);
                StaffUserResponse? user = users.FirstOrDefault(item => item.Id == userId);
                if (user is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                user.DisplayName = body.RootElement.GetProperty("displayName").GetString()!;
                user.Role = body.RootElement.GetProperty("role").GetString()!;
                user.RestaurantId = body.RootElement.GetProperty("restaurantId").GetGuid();
                user.AllRestaurants = body.RootElement.GetProperty("allRestaurants").GetBoolean();
                user.IsActive = body.RootElement.GetProperty("isActive").GetBoolean();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (
                path.StartsWith("/api/identity/users/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Delete
            )
            {
                Guid userId = Guid.Parse(path.Split('/')[4]);
                StaffUserResponse? user = users.FirstOrDefault(item => item.Id == userId);
                if (user is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                users.Remove(user);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (
                path.EndsWith("/tables", StringComparison.Ordinal)
                && request.Method == HttpMethod.Post
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                JsonElement tableDraft = body.RootElement;
                TableResponse created = new()
                {
                    Id = Guid.NewGuid(),
                    RestaurantId = Guid.Parse(path.Split('/')[4]),
                    ZoneId = tableDraft.GetProperty("zoneId").GetGuid(),
                    Code = tableDraft.GetProperty("code").GetString()!,
                    Label = tableDraft.GetProperty("label").GetString()!,
                    IsActive = true,
                };
                Tables.Add(created);
                return Json(created);
            }
            if (
                path.EndsWith("/zones", StringComparison.Ordinal)
                && request.Method == HttpMethod.Post
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                ZoneResponse created = new()
                {
                    Id = Guid.NewGuid(),
                    Name = body.RootElement.GetProperty("name").GetString()!,
                    RequestGuestCount = body
                        .RootElement.GetProperty("requestGuestCount")
                        .GetBoolean(),
                };
                Zones.Add(created);
                return Json(created);
            }
            if (
                path.Contains("/zones/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put
            )
            {
                ZoneResponse? zone = Zones.FirstOrDefault(item =>
                    path.EndsWith(item.Id.ToString(), StringComparison.Ordinal)
                );
                if (zone is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                zone.Name = body.RootElement.GetProperty("name").GetString()!;
                zone.RequestGuestCount = body
                    .RootElement.GetProperty("requestGuestCount")
                    .GetBoolean();
                return Json(zone);
            }
            if (path.StartsWith("/api/dining/tables/", StringComparison.Ordinal))
            {
                Guid tableId = Guid.Parse(path.Split('/')[4]);
                TableResponse? table = Tables.FirstOrDefault(item => item.Id == tableId);
                if (
                    request.Method == HttpMethod.Post
                    && path.EndsWith("/qr/rotate", StringComparison.Ordinal)
                    && table is not null
                )
                {
                    table.QrCode = "new-code";
                    return Json(
                        new TableQrResponse(
                            table.Id,
                            table.RestaurantId,
                            table.Code,
                            table.Label,
                            table.QrCode,
                            "/new-qr",
                            "/customer-qr"
                        )
                    );
                }
                if (request.Method == HttpMethod.Put && table is not null)
                {
                    using JsonDocument body = JsonDocument.Parse(
                        await request.Content!.ReadAsStringAsync(cancellationToken)
                    );
                    table.Label = body.RootElement.GetProperty("label").GetString()!;
                    table.ZoneId = body.RootElement.GetProperty("zoneId").GetGuid();
                    table.IsActive = body.RootElement.GetProperty("isActive").GetBoolean();
                    return Json(table);
                }
                if (request.Method == HttpMethod.Delete && table is not null)
                {
                    Tables.Remove(table);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            }
            if (path == "/api/catalog/products" && request.Method == HttpMethod.Post)
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                JsonElement draft = body.RootElement;
                ProductResponse created = new()
                {
                    Id = Guid.NewGuid(),
                    Sku = draft.GetProperty("sku").GetString()!,
                    Name = draft.GetProperty("name").GetString()!,
                    CategoryId = draft.GetProperty("categoryId").GetGuid(),
                    IsActive = true,
                    Version = 1,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                Products.Add(created);
                return Json(created);
            }
            if (
                path.StartsWith("/api/catalog/products/", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put
            )
            {
                if (RejectProductUpdates)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);

                string[] segments = path.Split('/');
                ProductResponse? product = Products.FirstOrDefault(item =>
                    item.Id == Guid.Parse(segments[4])
                );
                if (product is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                string value = await request.Content!.ReadAsStringAsync(cancellationToken);
                switch (segments[5])
                {
                    case "name":
                        product.Name = JsonSerializer.Deserialize<string>(value)!;
                        break;
                    case "category":
                        product.CategoryId = JsonSerializer.Deserialize<Guid>(value);
                        break;
                    case "active":
                        product.IsActive = JsonSerializer.Deserialize<bool>(value);
                        break;
                }
                product.Version++;
                product.UpdatedAtUtc = DateTime.UtcNow;
                return Json(product);
            }
            if (path.EndsWith("/provider", StringComparison.Ordinal))
                return Json(
                    new
                    {
                        activeProvider = "local",
                        microsoftConfigured = false,
                        googleConfigured = false,
                    }
                );
            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/stations", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                KitchenStationResponse created = new()
                {
                    Id = Guid.NewGuid(),
                    RestaurantId = station.RestaurantId,
                    Code = body.RootElement.GetProperty("code").GetString()!,
                    Name = body.RootElement.GetProperty("name").GetString()!,
                    IsActive = true,
                    PrintTicket = body.RootElement.GetProperty("printTicket").GetBoolean(),
                    Priority = body.RootElement.GetProperty("priority").GetInt32(),
                    Version = 1,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                Stations.Add(created);
                return Json(created);
            }
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/stations", StringComparison.Ordinal)
            )
            {
                StationReads++;
                return Json(Stations);
            }
            if (
                request.Method == HttpMethod.Put
                && path.Contains("/stations/", StringComparison.Ordinal)
            )
            {
                if (FailNextUpdate)
                {
                    FailNextUpdate = false;
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }
                if (path.EndsWith("/print-ticket", StringComparison.Ordinal))
                    station.PrintTicket = true;
                if (path.EndsWith("/primary", StringComparison.Ordinal))
                {
                    station.IsPrimary = true;
                    station.RequiresPrimaryDispatch = false;
                    station.Priority = 0;
                }
                if (path.EndsWith("/name", StringComparison.Ordinal))
                {
                    station.Name = JsonSerializer.Deserialize<string>(
                        await request.Content!.ReadAsStringAsync(cancellationToken)
                    )!;
                }
                if (path.EndsWith("/priority", StringComparison.Ordinal))
                {
                    station.Priority = JsonSerializer.Deserialize<int>(
                        await request.Content!.ReadAsStringAsync(cancellationToken)
                    );
                }
                station.Version++;
                return Json(station);
            }
            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/categories", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CreatedCategory = body.RootElement.Clone();
                CategoriesCreated++;
                return Json(
                    new CategoryResponse
                    {
                        Id = Guid.NewGuid(),
                        Code = "OTHER",
                        Name = "Otros",
                    }
                );
            }
            if (
                request.Method == HttpMethod.Put
                && path.StartsWith("/api/catalog/categories/", StringComparison.Ordinal)
            )
            {
                Guid categoryId = Guid.Parse(path.Split('/')[4]);
                CategoryResponse? category = Categories.FirstOrDefault(item =>
                    item.Id == categoryId
                );
                if (category is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                JsonElement updated = body.RootElement;
                category.Name = updated.GetProperty("name").GetString()!;
                category.DefaultStationCode = updated
                    .GetProperty("defaultStationCode")
                    .GetString()!;
                category.DefaultStationName = updated
                    .GetProperty("defaultStationName")
                    .GetString()!;
                category.RequiresKds = updated.GetProperty("requiresKds").GetBoolean();
                category.IsActive = updated.GetProperty("isActive").GetBoolean();
                category.ShowInPos = updated.GetProperty("showInPos").GetBoolean();
                category.ShowInCommander = updated.GetProperty("showInCommander").GetBoolean();
                category.ShowInCustomerQr = updated.GetProperty("showInCustomerQr").GetBoolean();
                category.Version++;
                category.UpdatedAtUtc = DateTime.UtcNow;
                return Json(category);
            }
            if (
                request.Method == HttpMethod.Get
                && (
                    path.EndsWith("/categories", StringComparison.Ordinal)
                    || path.EndsWith("/products", StringComparison.Ordinal)
                )
            )
            {
                if (path.EndsWith("/products", StringComparison.Ordinal))
                    return Json(Products);
                CategoryProjectionReads++;
                return Json(Categories);
            }
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/tables", StringComparison.Ordinal)
            )
                return Json(Tables);
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/zones", StringComparison.Ordinal)
            )
                return Json(Zones);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class ConfirmingJsRuntime(bool confirm) : IJSRuntime
    {
        public int Confirmations { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "confirm")
                Confirmations++;
            object? result = identifier == "confirm" ? confirm : default(TValue);
            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }
}
