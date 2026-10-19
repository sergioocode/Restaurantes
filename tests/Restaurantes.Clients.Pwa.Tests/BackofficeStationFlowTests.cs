using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
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
                return Json(Array.Empty<object>());
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
