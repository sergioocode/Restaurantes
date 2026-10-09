using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<CatalogDesignSnapshot> CatalogDesignAsync() =>
        GetAsync<CatalogDesignSnapshot>("/api/catalog/design");

    private async Task SaveDesignAsync(string path, object body)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Put, path);
        request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public Task SaveTaxAsync(CatalogTaxModel item) =>
        SaveDesignAsync($"/api/catalog/design/taxes/{item.Id}", item);

    public Task SaveAllergenAsync(CatalogAllergenModel item) =>
        SaveDesignAsync($"/api/catalog/design/allergens/{item.Id}", item);

    public Task SaveProductTaxAsync(Guid productId, Guid taxId) =>
        SaveDesignAsync($"/api/catalog/design/products/{productId}/tax", taxId);

    public Task SaveProductAllergensAsync(Guid productId, IEnumerable<Guid> ids) =>
        SaveDesignAsync($"/api/catalog/design/products/{productId}/allergens", ids.ToArray());

    public Task SaveProductDefinitionAsync(Guid productId, ProductDefinitionDraft draft) =>
        SaveDesignAsync($"/api/catalog/design/products/{productId}/definition", draft);

    public async Task UploadProductPhotoAsync(Guid productId, IBrowserFile photo)
    {
        if (photo.Size > 10_000_000)
            throw new InvalidOperationException("La foto supera 10 MB.");
        using MultipartFormDataContent body = new();
        AddFile(body, photo, "original", 10_000_000);
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/catalog/products/{productId}/photos"
        );
        request.Content = body;
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private static void AddFile(
        MultipartFormDataContent body,
        IBrowserFile file,
        string field,
        long maxBytes
    )
    {
        StreamContent content = new(file.OpenReadStream(maxBytes));
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        body.Add(content, field, field);
    }

    public Task SaveProductPricesAsync(
        Guid productId,
        Guid restaurantId,
        List<PriceDraft> prices
    ) => SaveDesignAsync($"/api/catalog/design/products/{productId}/prices/{restaurantId}", prices);

    public async Task<CatalogMenuModel> SaveCatalogMenuAsync(Guid menuId, CatalogMenuDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/design/menus/{menuId}"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CatalogMenuModel>(response);
    }

    public Task SetCatalogMenuActiveAsync(Guid menuId, bool active) =>
        SaveDesignAsync(
            $"/api/catalog/design/menus/{menuId}/{(active ? "active" : "inactive")}",
            new { }
        );

    public async Task DeleteLocalCatalogMenuAsync(Guid menuId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Delete,
            $"/api/catalog/design/menus/{menuId}"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }
}
