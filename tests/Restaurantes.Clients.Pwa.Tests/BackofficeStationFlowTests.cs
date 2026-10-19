using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeStationFlowTests : BunitContext
{
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
        public bool FailNextUpdate { get; set; }
        public List<string> Paths { get; } = [];
        public int StationReads { get; private set; }
        public int CategoriesCreated { get; private set; }
        public JsonElement? CreatedCategory { get; private set; }
        public int UserCreations { get; private set; }
        private readonly List<StaffUserResponse> users = [];
        public List<TableResponse> Tables { get; } = [];
        public List<ProductResponse> Products { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
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
                path.EndsWith("/tables", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get
            )
                return Json(Tables);
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
            if (path.StartsWith("/api/dining/tables/", StringComparison.Ordinal))
            {
                Guid tableId = Guid.Parse(path.Split('/')[4]);
                TableResponse? table = Tables.FirstOrDefault(item => item.Id == tableId);
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
                request.Method == HttpMethod.Get
                && path.EndsWith("/stations", StringComparison.Ordinal)
            )
            {
                StationReads++;
                return Json(new[] { station });
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
                request.Method == HttpMethod.Get
                && (
                    path.EndsWith("/categories", StringComparison.Ordinal)
                    || path.EndsWith("/products", StringComparison.Ordinal)
                )
            )
                return path.EndsWith("/products", StringComparison.Ordinal)
                    ? Json(Products)
                    : Json(Array.Empty<object>());
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/tables", StringComparison.Ordinal)
            )
                return Json(Array.Empty<object>());
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/zones", StringComparison.Ordinal)
            )
                return Json(Array.Empty<object>());
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
