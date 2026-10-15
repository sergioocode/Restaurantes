using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<CompanyResponse>> CompaniesAsync()
    {
        return GetAsync<List<CompanyResponse>>("/api/restaurant-operations/companies");
    }

    public async Task<CompanyResponse> CreateCompanyAsync(CompanyDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            "/api/restaurant-operations/companies"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CompanyResponse>(response);
    }

    public async Task<CompanyResponse> UpdateCompanyAsync(CompanyResponse company)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/restaurant-operations/companies/{company.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                company.LegalName,
                company.TaxId,
                company.FiscalAddress,
                company.Phone,
                company.Email,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CompanyResponse>(response);
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
        request.Content = JsonContent.Create(new { item.Name, item.Address });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }

    public async Task<RestaurantResponse> SetRestaurantCompanyAsync(
        Guid restaurantId,
        Guid companyId
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/restaurant-operations/restaurants/{restaurantId}/company"
        );
        request.Content = JsonContent.Create(new { CompanyId = companyId });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }

    public async Task<RestaurantResponse> SetRestaurantActiveAsync(Guid restaurantId, bool isActive)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/restaurant-operations/restaurants/{restaurantId}/active"
        );
        request.Content = JsonContent.Create(new { IsActive = isActive });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }
}
