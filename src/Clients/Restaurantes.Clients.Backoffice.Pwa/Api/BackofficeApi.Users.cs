using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<StaffUserResponse>> UsersAsync()
    {
        return GetAsync<List<StaffUserResponse>>("/api/identity/users");
    }

    public async Task CreateUserAsync(StaffUserDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/identity/users");
        request.Content = JsonContent.Create(
            new
            {
                draft.Email,
                draft.DisplayName,
                draft.Role,
                AllRestaurants = draft.RestaurantId is null,
                draft.RestaurantId,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public async Task UpdateUserAsync(StaffUserResponse user)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/identity/users/{user.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                user.DisplayName,
                user.Role,
                AllRestaurants = user.RestaurantId is null,
                user.RestaurantId,
                user.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }
}
