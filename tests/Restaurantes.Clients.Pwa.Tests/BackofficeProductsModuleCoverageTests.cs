using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Syncfusion.Blazor;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeProductsModuleCoverageTests : BunitContext
{
    [Fact]
    public async Task Catalog_design_groups_products_and_rolls_back_a_failed_local_availability_change()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        ProductResponse availableProduct = new()
        {
            Id = Guid.NewGuid(),
            Sku = "CAF-01",
            Name = "Café",
            CategoryId = categoryId,
            IsActive = true,
        };
        ProductResponse inactiveProduct = new()
        {
            Id = Guid.NewGuid(),
            Sku = "PAU-01",
            Name = "Pan fuera de categoría",
            CategoryId = Guid.NewGuid(),
            IsActive = false,
        };
        ProductResponse productWithoutLocalAvailability = new()
        {
            Id = Guid.NewGuid(),
            Sku = "BEB-02",
            Name = "Producto sin disponibilidad local",
            CategoryId = Guid.NewGuid(),
            IsActive = true,
        };
        CatalogDesignSnapshot snapshot = new()
        {
            Products = [availableProduct, inactiveProduct, productWithoutLocalAvailability],
            ProductPhotos =
            [
                new ProductPhotoModel
                {
                    ProductId = availableProduct.Id,
                    Revision = Guid.NewGuid(),
                },
            ],
            LocalAvailability =
            [
                new RestaurantProductAvailabilityModel
                {
                    RestaurantId = restaurantId,
                    ProductId = availableProduct.Id,
                    IsAvailable = true,
                },
                new RestaurantProductAvailabilityModel
                {
                    RestaurantId = restaurantId,
                    ProductId = inactiveProduct.Id,
                    IsAvailable = true,
                },
            ],
        };
        RecordingHandler handler = new(new ProductResponse(), snapshot);
        handler.RejectAvailability = true;
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSyncfusionBlazor();
        BackofficeHome host = new();
        SetField(host, "module", "test");
        SetField(host, "restaurantId", restaurantId);
        SetField(
            host,
            "categories",
            new List<CategoryResponse>
            {
                new()
                {
                    Id = categoryId,
                    Code = "BEB",
                    Name = "Bebidas",
                    IsActive = true,
                },
            }
        );

        IRenderedComponent<CatalogDesignModule> cut = Render<CatalogDesignModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );
        cut.WaitForAssertion(() => Assert.Contains("Pan fuera de categoría", cut.Markup));
        Assert.Contains("Producto sin disponibilidad local", cut.Markup);

        Assert.Contains("Bebidas", cut.Markup);
        Assert.Contains("Sin categoría", cut.Markup);
        Assert.Contains($"/photos/small?v={snapshot.ProductPhotos[0].Revision}", cut.Markup);
        IReadOnlyList<AngleSharp.Dom.IElement> availability = cut.FindAll(
            ".local-product-maintenance input[type=checkbox]"
        );
        Assert.True(availability[0].HasAttribute("checked"));
        Assert.True(availability[1].HasAttribute("disabled"));
        Assert.False(availability[2].HasAttribute("checked"));
        Assert.False(availability[2].HasAttribute("disabled"));
        await availability[0].ChangeAsync(false);
        Assert.True(
            cut.Find(".local-product-maintenance input[type=checkbox]").HasAttribute("checked")
        );
        Assert.Contains("API 400", cut.Markup);
        Assert.Contains(
            $"/api/catalog/design/restaurants/{restaurantId}/products/{availableProduct.Id}/availability",
            handler.Paths
        );

        handler.FailCatalogDesign = true;
        await cut.InvokeAsync(() => InvokeRefresh(cut.Instance));
        Assert.Contains("API 400", cut.Markup);
    }

    [Fact]
    public async Task Products_module_renders_photo_and_saves_name_and_allergen_definition()
    {
        Guid categoryId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid allergenId = Guid.NewGuid();
        ProductResponse product = new()
        {
            Id = productId,
            Sku = "CAF-01",
            Name = "Café",
            CategoryId = categoryId,
            IsActive = true,
            Version = 1,
        };
        CatalogDesignSnapshot snapshot = new()
        {
            ProductPhotos =
            [
                new ProductPhotoModel { ProductId = productId, Revision = Guid.NewGuid() },
            ],
            Allergens =
            [
                new CatalogAllergenModel
                {
                    Id = allergenId,
                    Code = "LACTOSA",
                    Name = "Leche",
                    IsActive = true,
                },
            ],
        };
        RecordingHandler handler = new(product, snapshot);
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSyncfusionBlazor();
        Services.AddSingleton(new SessionAuthenticationStateProvider());
        IRenderedComponent<BackofficeHome> hostComponent = Render<BackofficeHome>();
        hostComponent.WaitForState(() => !GetField<bool>(hostComponent.Instance, "initializing"));
        BackofficeHome host = hostComponent.Instance;
        typeof(BackofficeHome)
            .GetField("login", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                host,
                new LoginResponse(
                    "token",
                    DateTime.UtcNow.AddHours(1),
                    new LoginUser(Guid.NewGuid(), "admin", "Administrador"),
                    [],
                    ["Admin"],
                    ["catalog.global.manage"]
                )
            );
        typeof(BackofficeHome)
            .GetField("categories", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                host,
                new List<CategoryResponse>
                {
                    new()
                    {
                        Id = categoryId,
                        Code = "BEB",
                        Name = "Bebidas",
                        IsActive = true,
                    },
                }
            );
        typeof(BackofficeHome)
            .GetField("products", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, new List<ProductResponse> { product });

        IRenderedComponent<ProductsModule> cut = Render<ProductsModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );
        cut.WaitForAssertion(() => Assert.Contains("Café", cut.Markup));
        Assert.Contains("/photos/small", cut.Markup);
        Assert.Contains("Bebidas", cut.Markup);

        cut.Find(".product-name-editor input").Change("Café cortado");
        await cut.Find(".product-name-editor button").ClickAsync();
        Assert.Equal("Café cortado", product.Name);
        Assert.Contains("/api/catalog/products/" + productId + "/name", handler.Paths);

        await cut.FindAll(".product-actions button")[1].ClickAsync();
        await cut.Find(".product-dialog-options input[type=checkbox]").ChangeAsync(true);
        await cut.Find(".product-dialog footer .primary").ClickAsync();

        Assert.Contains("/api/catalog/design/products/" + productId + "/allergens", handler.Paths);
        Assert.Equal([allergenId], handler.SavedAllergens);
    }

    private static T GetField<T>(object instance, string name) =>
        (T)
            instance
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(instance)!;

    private static Task InvokeRefresh(CatalogDesignModule component) =>
        (Task)
            component
                .GetType()
                .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, null)!;

    private sealed class RecordingHandler(ProductResponse product, CatalogDesignSnapshot snapshot)
        : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<Guid> SavedAllergens { get; private set; } = [];
        public bool RejectAvailability { get; set; }
        public bool FailCatalogDesign { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (request.Method == HttpMethod.Get && path == "/api/catalog/design")
                return FailCatalogDesign
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    : Json(snapshot);
            if (
                request.Method == HttpMethod.Put
                && path.Contains("/availability", StringComparison.Ordinal)
            )
                return RejectAvailability
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            if (
                request.Method == HttpMethod.Put
                && path.EndsWith("/name", StringComparison.Ordinal)
            )
            {
                string? name = await request.Content!.ReadFromJsonAsync<string>(cancellationToken);
                product.Name = name ?? product.Name;
                return Json(product);
            }
            if (
                request.Method == HttpMethod.Put
                && path.EndsWith("/allergens", StringComparison.Ordinal)
            )
            {
                SavedAllergens = (
                    await request.Content!.ReadFromJsonAsync<Guid[]>(cancellationToken)
                )!.ToList();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(value) };
    }

    private static void SetField(object instance, string name, object value) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
}
