using System.Net;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class ProductSettingsCoverageTests : BunitContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );
    private readonly RecordingHandler handler = new();

    public ProductSettingsCoverageTests()
    {
        Services.AddSingleton(
            new BackofficeApi(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") })
        );
    }

    [Fact]
    public async Task Tax_selection_saves_refreshes_and_restores_the_previous_value_on_failure()
    {
        Guid productId = Guid.NewGuid();
        Guid taxId = Guid.NewGuid();
        int refreshes = 0;
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest));
        IRenderedComponent<ProductSettings> cut = RenderSettings(
            productId,
            snapshot: new CatalogDesignSnapshot
            {
                Taxes =
                [
                    new CatalogTaxModel
                    {
                        Id = taxId,
                        Name = "IVA",
                        IsActive = true,
                    },
                ],
            },
            onSaved: EventCallback.Factory.Create(this, () => refreshes++)
        );

        await cut.InvokeAsync(() => cut.Find("select.product-tax-select").Change(taxId.ToString()));

        Assert.Equal(1, refreshes);
        Assert.Equal($"/api/catalog/design/products/{productId}/tax", handler.Requests[0].Path);
        Assert.Equal("application/json", handler.Requests[0].ContentType);
        Assert.Contains(taxId.ToString(), handler.Requests[0].Body);

        await cut.InvokeAsync(() =>
            cut.Find("select.product-tax-select").Change(Guid.NewGuid().ToString())
        );

        Assert.Equal(taxId.ToString(), cut.Find("select.product-tax-select").GetAttribute("value"));
        Assert.Contains("API 400", cut.Find("[role=alert]").TextContent);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Product_panels_save_allergens_extras_and_merged_prices()
    {
        Guid productId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid extraCategoryId = Guid.NewGuid();
        Guid existingExtraId = Guid.NewGuid();
        Guid newExtraId = Guid.NewGuid();
        Guid allergenId = Guid.NewGuid();
        Guid inactiveAllergenId = Guid.NewGuid();
        Guid localPriceId = Guid.NewGuid();
        Guid remotePriceId = Guid.NewGuid();
        CatalogDesignSnapshot snapshot = new()
        {
            Allergens =
            [
                new CatalogAllergenModel { Id = allergenId, Name = "Gluten" },
                new CatalogAllergenModel
                {
                    Id = inactiveAllergenId,
                    Name = "Inactivo",
                    IsActive = false,
                },
            ],
            ProductAllergens =
            [
                new ProductAllergenModel { ProductId = productId, AllergenId = allergenId },
            ],
            Configurations =
            [
                new ProductConfigurationModel { ProductId = productId, IsCombo = true },
            ],
            Groups =
            [
                new ProductOptionGroupModel
                {
                    Id = Guid.NewGuid(),
                    ParentProductId = productId,
                    Name = "Elegir",
                    Kind = "Choice",
                    SortOrder = 1,
                },
                new ProductOptionGroupModel
                {
                    Id = Guid.NewGuid(),
                    ParentProductId = productId,
                    Name = "Extras",
                    Kind = "Extra",
                    SortOrder = 2,
                },
            ],
            Prices =
            [
                new CatalogPriceModel
                {
                    Id = localPriceId,
                    ProductId = productId,
                    Channel = "LOCAL",
                    Format = "PLATO",
                    Amount = 10,
                },
                new CatalogPriceModel
                {
                    Id = remotePriceId,
                    ProductId = productId,
                    Channel = "DELIVERY",
                    Format = "PLATO",
                    Amount = 12,
                },
            ],
        };
        ProductResponse product = new()
        {
            Id = productId,
            Name = "Pizza",
            CategoryId = categoryId,
        };
        BackofficeHome host = CreateHost(
            [
                new CategoryResponse
                {
                    Id = categoryId,
                    Code = "PIZZA",
                    Name = "Pizzas",
                },
                new CategoryResponse
                {
                    Id = extraCategoryId,
                    Code = "EXTRAS",
                    Name = "Complementos",
                },
            ],
            [
                product,
                new ProductResponse
                {
                    Id = existingExtraId,
                    Name = "Aceitunas",
                    CategoryId = extraCategoryId,
                },
                new ProductResponse
                {
                    Id = newExtraId,
                    Name = "Bacon",
                    CategoryId = extraCategoryId,
                },
                new ProductResponse
                {
                    Id = Guid.NewGuid(),
                    Name = "Agua",
                    CategoryId = categoryId,
                },
            ]
        );
        IRenderedComponent<ProductSettings> cut = Render<ProductSettings>(parameters =>
            parameters
                .Add(component => component.Product, product)
                .Add(component => component.Host, host)
                .Add(component => component.Snapshot, snapshot)
        );

        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent.Contains("Alérgenos")).Click()
        );
        await cut.InvokeAsync(() => cut.Find("input[type=checkbox]").Change(false));
        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent == "Guardar").Click()
        );
        Assert.EndsWith($"/products/{productId}/allergens", handler.Requests[^1].Path);
        Assert.Empty(cut.FindAll(".product-dialog"));

        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent.Contains("Extras")).Click()
        );
        Assert.Contains("Aceitunas", cut.Markup);
        Assert.Contains("Bacon", cut.Markup);
        Assert.DoesNotContain("Agua", cut.Markup);
        await cut.InvokeAsync(() => cut.FindAll("input[type=checkbox]")[1].Change(true));
        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent == "Guardar").Click()
        );
        Assert.EndsWith($"/products/{productId}/definition", handler.Requests[^1].Path);
        ProductDefinitionDraft definition = JsonSerializer.Deserialize<ProductDefinitionDraft>(
            handler.Requests[^1].Body,
            SerializerOptions
        )!;
        Assert.True(definition.IsCombo);
        Assert.True(definition.AllowsExtras);
        Assert.Contains(definition.Groups, group => group.Kind == "Choice");
        Assert.Contains(
            definition.Groups,
            group => group.Kind == "Extra" && group.ProductIds.Contains(newExtraId)
        );

        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent.Contains("Precios")).Click()
        );
        await cut.InvokeAsync(() =>
            cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Añadir formato"))
                .Click()
        );
        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent == "Guardar").Click()
        );
        Assert.EndsWith($"/products/{productId}/prices/{Guid.Empty}", handler.Requests[^1].Path);
        List<PriceDraft> prices = JsonSerializer.Deserialize<List<PriceDraft>>(
            handler.Requests[^1].Body,
            SerializerOptions
        )!;
        Assert.Contains(prices, price => price.Channel == "DELIVERY" && price.Amount == 12);
        Assert.Contains(prices, price => price.Channel == "LOCAL" && price.Format == "PLATO");
    }

    [Fact]
    public async Task Read_only_product_does_not_save_tax_or_catalog_changes()
    {
        Guid productId = Guid.NewGuid();
        IRenderedComponent<ProductSettings> cut = RenderSettings(productId, readOnly: true);

        await cut.InvokeAsync(() =>
            cut.Find("select.product-tax-select").Change(Guid.NewGuid().ToString())
        );
        await cut.InvokeAsync(() =>
            cut.FindAll("button").Single(button => button.TextContent.Contains("Alérgenos")).Click()
        );
        Assert.True(cut.Find("button.primary").HasAttribute("disabled"));
        await cut.InvokeAsync(() => cut.Find("button.primary").Click());

        Assert.Empty(handler.Requests);
    }

    private IRenderedComponent<ProductSettings> RenderSettings(
        Guid productId,
        CatalogDesignSnapshot? snapshot = null,
        bool readOnly = false,
        EventCallback onSaved = default
    )
    {
        ProductResponse product = new() { Id = productId, Name = "Producto" };
        return Render<ProductSettings>(parameters =>
            parameters
                .Add(component => component.Product, product)
                .Add(component => component.Host, CreateHost([], [product]))
                .Add(component => component.Snapshot, snapshot ?? new CatalogDesignSnapshot())
                .Add(component => component.ReadOnly, readOnly)
                .Add(component => component.OnSaved, onSaved)
        );
    }

    private static BackofficeHome CreateHost(
        List<CategoryResponse> categories,
        List<ProductResponse> products
    )
    {
        BackofficeHome host = new();
        typeof(BackofficeHome)
            .GetField("categories", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, categories);
        typeof(BackofficeHome)
            .GetField("products", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, products);
        return host;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public Queue<HttpResponseMessage> Responses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(
                new RecordedRequest(
                    request.RequestUri!.AbsolutePath,
                    request.Content?.Headers.ContentType?.MediaType ?? "",
                    request.Content is null
                        ? ""
                        : await request.Content.ReadAsStringAsync(cancellationToken)
                )
            );
            return Responses.Count > 0
                ? Responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed record RecordedRequest(string Path, string ContentType, string Body);
}
