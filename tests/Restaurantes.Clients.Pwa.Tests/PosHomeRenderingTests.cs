using System.Net;
using System.Reflection;
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

public sealed class PosHomeRenderingTests : BunitContext
{
    [Fact]
    public async Task Authenticated_pos_filters_tables_and_shows_available_catalog_items()
    {
        using HttpClient http = new(new EmptyHandler())
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton(new CatalogRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
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
            "restaurants",
            new List<RestaurantResponse>
            {
                new(restaurantId, "CENTRO", "Centro", "Calle Mayor", true),
            }
        );
        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Available",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M2",
                    "Mesa 2",
                    true,
                    "Occupied",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
            }
        );

        await Rerender(cut);
        Assert.Contains("Salón", cut.Markup);
        cut.FindAll(".tables-toolbar button")
            .Single(button => button.TextContent.Contains("Salón"))
            .Click();
        Assert.Equal(2, cut.FindAll(".dining-location").Count);

        cut.FindAll(".table-status-filters button")
            .Single(button => button.TextContent == "Disponibles")
            .Click();
        Assert.Single(cut.FindAll(".dining-location"));
        Assert.Contains("Mesa 1", cut.Markup);
        Assert.DoesNotContain("Mesa 2", cut.Markup);

        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Available",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
            }
        );
        cut.FindAll(".table-status-filters button")
            .Single(button => button.TextContent == "Ocupadas")
            .Click();
        Assert.Contains("No hay ubicaciones para este filtro.", cut.Markup);

        SetField(cut.Instance, "tables", new List<TableResponse>());
        SetField(cut.Instance, "selectedZoneId", null!);
        await Rerender(cut);
        Assert.Contains("No hay sectores configurados", cut.Markup);

        SetField(
            cut.Instance,
            "menu",
            new List<MenuItemResponse>
            {
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "CAFE",
                    "Café",
                    Guid.NewGuid(),
                    "BEB",
                    "Bebidas",
                    2m,
                    true,
                    "BAR",
                    "Bar",
                    DefaultFormat: "UNIDAD"
                ),
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "AGUA",
                    "Agua",
                    Guid.NewGuid(),
                    "BEB",
                    "Bebidas",
                    1m,
                    false,
                    "BAR",
                    "Bar",
                    DefaultFormat: "UNIDAD"
                ),
            }
        );
        SetField(cut.Instance, "takeawayOpen", true);
        SetField(cut.Instance, "serviceMode", "Takeaway");
        await Rerender(cut);

        Assert.Contains("Café", cut.Markup);
        Assert.DoesNotContain("Agua", cut.Markup);
        Assert.Single(cut.FindAll(".product-tile"));
        Assert.Contains("Bebidas", cut.Markup);

        SetField(cut.Instance, "menu", new List<MenuItemResponse>());
        await Rerender(cut);
        Assert.Contains("No hay productos en este formato.", cut.Markup);
    }

    private static Task Rerender(IRenderedComponent<PosHome> cut) =>
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.Empty));

    private static void SetField(object component, string name, object value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class EmptyJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(default(TValue)!);
    }
}
