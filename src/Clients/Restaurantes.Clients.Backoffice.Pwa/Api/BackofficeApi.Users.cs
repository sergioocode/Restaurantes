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
}
