using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Restaurantes.Clients.Backoffice.Pwa;

public sealed class BackofficeApi(HttpClient http)
{
    public string? AccessToken { get; set; }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/identity/login",
            new { username, password }
        );
        return await ReadAsync<LoginResponse>(response);
    }

    public Task<List<StaffUserResponse>> UsersAsync()
    {
        return GetAsync<List<StaffUserResponse>>("/api/identity/users");
    }

    public async Task CreateUserAsync(StaffUserDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/identity/users");
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public async Task SaveUserAssignmentAsync(Guid userId, UserRestaurantAssignmentResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/identity/users/{userId}/restaurants/{item.RestaurantId}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Role,
                item.IsActive,
                item.ValidFromUtc,
                item.ValidUntilUtc,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public async Task AddUserAssignmentAsync(Guid userId, UserAssignmentDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/identity/users/{userId}/restaurants"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public Task<List<CategoryResponse>> CategoriesAsync()
    {
        return GetAsync<List<CategoryResponse>>("/api/catalog/categories");
    }

    public Task<List<ProductResponse>> ProductsAsync()
    {
        return GetAsync<List<ProductResponse>>("/api/catalog/products");
    }

    public Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        return GetAsync<List<MenuItemResponse>>(
            $"/api/catalog/restaurants/{restaurantId}/menu/configuration"
        );
    }

    public Task<List<KitchenStationResponse>> StationsAsync(Guid restaurantId)
    {
        return GetAsync<List<KitchenStationResponse>>(
            $"/api/catalog/restaurants/{restaurantId}/stations"
        );
    }

    public async Task EnsureDefaultStationsAsync(Guid restaurantId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/catalog/restaurants/{restaurantId}/stations/defaults"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await ReadAsync<List<KitchenStationResponse>>(response);
    }

    public async Task<KitchenStationResponse> CreateStationAsync(
        Guid restaurantId,
        KitchenStationDraft draft
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/catalog/restaurants/{restaurantId}/stations"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<KitchenStationResponse>(response);
    }

    public async Task<KitchenStationResponse> UpdateStationAsync(KitchenStationResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/restaurants/{item.RestaurantId}/stations/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.IsActive,
                item.IsPrimary,
                item.RequiresPrimaryDispatch,
                item.Priority,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<KitchenStationResponse>(response);
    }

    public async Task<MenuItemResponse> ConfigureAsync(
        Guid restaurantId,
        Guid productId,
        MenuEditor editor
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/restaurants/{restaurantId}/products/{productId}"
        );
        request.Content = JsonContent.Create(
            new
            {
                editor.Price,
                editor.IsAvailable,
                editor.PreparationStationCode,
                editor.PreparationStationName,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<MenuItemResponse>(response);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(CategoryDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/catalog/categories");
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CategoryResponse>(response);
    }

    public async Task<ProductResponse> CreateProductAsync(ProductDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/catalog/products");
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }

    public async Task<CategoryResponse> UpdateCategoryAsync(CategoryResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/categories/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.DefaultStationCode,
                item.DefaultStationName,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CategoryResponse>(response);
    }

    public async Task<ProductResponse> UpdateProductAsync(ProductResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/products/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.CategoryId,
                item.BasePrice,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }

    public Task<List<RestaurantResponse>> RestaurantsAsync()
    {
        return GetAsync<List<RestaurantResponse>>("/api/restaurant-operations/restaurants");
    }

    public async Task<RestaurantResponse> CreateRestaurantAsync(RestaurantDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            "/api/restaurant-operations/restaurants"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }

    public async Task<RestaurantResponse> UpdateRestaurantAsync(RestaurantResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/restaurant-operations/restaurants/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.Address,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }

    public Task<List<ZoneResponse>> ZonesAsync(Guid restaurantId)
    {
        return GetAsync<List<ZoneResponse>>($"/api/dining/restaurants/{restaurantId}/zones");
    }

    public async Task<ZoneResponse> SaveZoneAsync(Guid restaurantId, ZoneResponse zone)
    {
        string path = $"/api/dining/restaurants/{restaurantId}/zones";
        using HttpRequestMessage request = Authorized(
            zone.Id == Guid.Empty ? HttpMethod.Post : HttpMethod.Put,
            zone.Id == Guid.Empty ? path : $"{path}/{zone.Id}"
        );
        request.Content = JsonContent.Create(new { zone.Name, zone.SortOrder });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ZoneResponse>(response);
    }

    public async Task DeleteZoneAsync(Guid restaurantId, Guid zoneId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Delete,
            $"/api/dining/restaurants/{restaurantId}/zones/{zoneId}"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await ReadAsync<object>(response);
        }
    }

    public Task<List<TableResponse>> TablesAsync(Guid restaurantId)
    {
        return GetAsync<List<TableResponse>>($"/api/dining/restaurants/{restaurantId}/tables");
    }

    public Task<DiningPolicyResponse> DiningPolicyAsync(Guid restaurantId)
    {
        return GetAsync<DiningPolicyResponse>($"/api/dining/restaurants/{restaurantId}/policy");
    }

    public async Task<TableResponse> CreateTableAsync(Guid restaurantId, TableDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/restaurants/{restaurantId}/tables"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableResponse>(response);
    }

    public async Task<TableResponse> UpdateTableAsync(TableResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/tables/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Label,
                item.ZoneId,
                item.RequestGuestCount,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableResponse>(response);
    }

    public async Task DeleteTableAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Delete,
            $"/api/dining/tables/{tableId}"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await ReadAsync<object>(response);
        }
    }

    public async Task<TableQrResponse> RotateQrAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/tables/{tableId}/qr/rotate"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableQrResponse>(response);
    }

    public async Task<DiningPolicyResponse> UpdateDiningPolicyAsync(
        Guid restaurantId,
        DiningPolicyResponse policy
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/restaurants/{restaurantId}/policy"
        );
        request.Content = JsonContent.Create(
            new
            {
                policy.QrRequiresImmediatePayment,
                policy.RequireTrustedNetworkForQr,
                policy.TakeawayRequiresPrepayment,
                policy.AllowCheckoutBeforeKitchenCompletion,
                policy.QrAllowedNetworks,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningPolicyResponse>(response);
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, path);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<T>(response);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("La API devolvió una respuesta vacía.");
        }
        string content = await response.Content.ReadAsStringAsync();
        string detail = content;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (
                json.RootElement.TryGetProperty("detail", out JsonElement value)
                || json.RootElement.TryGetProperty("title", out value)
            )
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ReadAsync<object>(response);
    }
}

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    LoginUser User,
    List<RestaurantAccess> Restaurants,
    List<string>? GlobalRoles = null
);

public sealed record LoginUser(Guid Id, string Username, string DisplayName);

public sealed record RestaurantAccess(Guid RestaurantId, string Role, List<string> Permissions);

public sealed class StaffUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<UserRestaurantAssignmentResponse> Restaurants { get; set; } = [];
}

public sealed class UserRestaurantAssignmentResponse
{
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
    public bool IsActive { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}

public sealed class StaffUserDraft
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
}

public sealed class UserAssignmentDraft
{
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}

public sealed class CategoryResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ProductResponse
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record MenuItemResponse(
    Guid RestaurantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    decimal Price,
    bool IsAvailable,
    string PreparationStationCode,
    string PreparationStationName,
    int Version,
    DateTime UpdatedAtUtc
);

public sealed class MenuEditor
{
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public string PreparationStationCode { get; set; } = "GENERAL";
    public string PreparationStationName { get; set; } = "General";
}

public sealed class KitchenStationResponse
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool RequiresPrimaryDispatch { get; set; }
    public int Priority { get; set; } = 1;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class KitchenStationDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool RequiresPrimaryDispatch { get; set; }
    public int Priority { get; set; } = 1;
}

public sealed class CategoryDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
}

public sealed class ProductDraft
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
}

public sealed class RestaurantResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class RestaurantDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public sealed class TableResponse
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? QrCode { get; set; }
    public string? QrPath { get; set; }
    public string? CustomerQrPath { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = "Available";
    public Guid? ActiveSessionId { get; set; }
}

public sealed class TableDraft
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed record TableQrResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    string QrCode,
    string QrPath,
    string CustomerQrPath
);

public sealed class DiningPolicyResponse
{
    public Guid RestaurantId { get; set; }
    public bool QrRequiresImmediatePayment { get; set; }
    public bool RequireTrustedNetworkForQr { get; set; }
    public bool TakeawayRequiresPrepayment { get; set; }
    public bool AllowCheckoutBeforeKitchenCompletion { get; set; }
    public string[] QrAllowedNetworks { get; set; } = [];
    public int Version { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class ZoneResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
