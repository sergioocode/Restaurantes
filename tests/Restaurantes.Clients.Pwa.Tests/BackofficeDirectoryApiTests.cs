using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeDirectoryApiTests
{
    [Fact]
    public async Task Company_and_restaurant_operations_send_expected_routes_and_values()
    {
        Guid companyId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        CompanyResponse company = new() { Id = companyId, LegalName = "Restaurantes SL" };
        RestaurantResponse restaurant = new() { Id = restaurantId, Name = "Centro" };
        RecordingHandler handler = new(
            Json(new List<CompanyResponse>()),
            Json(company),
            Json(company),
            Json(new List<RestaurantResponse>()),
            Json(restaurant),
            Json(restaurant),
            Json(restaurant),
            Json(restaurant)
        );
        BackofficeApi api = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
        )
        {
            AccessToken = "test-token",
        };

        Assert.Empty(await api.CompaniesAsync());
        CompanyResponse createdCompany = await api.CreateCompanyAsync(
            new CompanyDraft
            {
                LegalName = "Restaurantes SL",
                TaxId = "B12345678",
                FiscalAddress = "Calle Mayor 1",
            }
        );
        Assert.Equal(companyId, createdCompany.Id);
        Assert.Equal(companyId, (await api.UpdateCompanyAsync(company)).Id);
        Assert.Empty(await api.RestaurantsAsync());
        Assert.Equal(
            restaurantId,
            (
                await api.CreateRestaurantAsync(
                    new RestaurantDraft
                    {
                        Code = "CENTRO",
                        Name = "Centro",
                        Address = "Calle Mayor 1",
                        CompanyId = companyId,
                    }
                )
            ).Id
        );
        Assert.Equal(restaurantId, (await api.UpdateRestaurantAsync(restaurant)).Id);
        Assert.Equal(
            restaurantId,
            (await api.SetRestaurantCompanyAsync(restaurantId, companyId)).Id
        );
        Assert.Equal(restaurantId, (await api.SetRestaurantActiveAsync(restaurantId, false)).Id);

        Assert.Equal(
            [
                (HttpMethod.Get, "/api/restaurant-operations/companies"),
                (HttpMethod.Post, "/api/restaurant-operations/companies"),
                (HttpMethod.Put, $"/api/restaurant-operations/companies/{companyId}"),
                (HttpMethod.Get, "/api/restaurant-operations/restaurants"),
                (HttpMethod.Post, "/api/restaurant-operations/restaurants"),
                (HttpMethod.Put, $"/api/restaurant-operations/restaurants/{restaurantId}"),
                (HttpMethod.Put, $"/api/restaurant-operations/restaurants/{restaurantId}/company"),
                (HttpMethod.Put, $"/api/restaurant-operations/restaurants/{restaurantId}/active"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
        Assert.All(
            handler.Requests,
            request => Assert.Equal("Bearer test-token", request.Authorization)
        );
        Assert.Contains("\"companyId\":", handler.Requests[4].Body);
        Assert.Contains("\"companyId\":", handler.Requests[6].Body);
        Assert.Contains("\"isActive\":false", handler.Requests[7].Body);
    }

    [Fact]
    public async Task User_operations_distinguish_global_and_restaurant_scopes()
    {
        Guid userId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        RecordingHandler handler = new(
            Json(new List<StaffUserResponse>()),
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.NoContent)
        );
        BackofficeApi api = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
        );

        Assert.Empty(await api.UsersAsync());
        await api.CreateUserAsync(
            new StaffUserDraft
            {
                Email = "global@example.test",
                DisplayName = "Global",
                Role = "Admin",
            }
        );
        await api.UpdateUserAsync(
            new StaffUserResponse
            {
                Id = userId,
                DisplayName = "Camarero",
                Role = "Camarero",
                RestaurantId = restaurantId,
                IsActive = false,
            }
        );
        await api.DeleteUserAsync(userId);

        Assert.Equal("/api/identity/users", handler.Requests[0].Path);
        Assert.Contains("\"allRestaurants\":true", handler.Requests[1].Body);
        Assert.Contains(restaurantId.ToString(), handler.Requests[2].Body);
        Assert.Contains("\"allRestaurants\":false", handler.Requests[2].Body);
        Assert.Contains("\"isActive\":false", handler.Requests[2].Body);
        Assert.Equal($"/api/identity/users/{userId}", handler.Requests[3].Path);
        Assert.Equal(HttpMethod.Delete, handler.Requests[3].Method);
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new RecordedRequest(
                    request.Method,
                    request.RequestUri!.AbsolutePath,
                    body,
                    request.Headers.Authorization?.ToString()
                )
            );
            return remaining.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Body,
        string? Authorization
    );
}
