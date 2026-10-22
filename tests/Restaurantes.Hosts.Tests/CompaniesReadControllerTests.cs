using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Api.Controllers;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class CompaniesReadControllerTests
{
    [Fact]
    public async Task User_without_backoffice_permission_is_forbidden_without_loading_companies()
    {
        CompanyReadStore companies = new();
        RestaurantReadStore restaurants = new();
        CompaniesReadController controller = CreateController(
            companies,
            restaurants,
            new ClaimsPrincipal(new ClaimsIdentity([], "test"))
        );

        ActionResult<IReadOnlyList<CompanyResponse>> result = await controller.List(
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal(0, companies.ReadCount);
        Assert.Equal(0, restaurants.ReadCount);
    }

    [Fact]
    public async Task Global_admin_receives_every_company_without_loading_restaurants()
    {
        CompanyResponse[] values = [Company(), Company()];
        CompanyReadStore companies = new(values);
        RestaurantReadStore restaurants = new();
        ClaimsPrincipal user = new(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")
        );
        CompaniesReadController controller = CreateController(companies, restaurants, user);

        ActionResult<IReadOnlyList<CompanyResponse>> result = await controller.List(
            CancellationToken.None
        );

        Assert.Equal(
            values,
            Assert.IsType<CompanyResponse[]>(Assert.IsType<OkObjectResult>(result.Result).Value)
        );
        Assert.Equal(1, companies.ReadCount);
        Assert.Equal(0, restaurants.ReadCount);
    }

    [Fact]
    public async Task Local_permission_returns_only_companies_of_accessible_restaurants()
    {
        Guid visibleRestaurantId = Guid.NewGuid();
        Guid hiddenRestaurantId = Guid.NewGuid();
        Guid visibleCompanyId = Guid.NewGuid();
        Guid hiddenCompanyId = Guid.NewGuid();
        CompanyResponse[] values = [Company(visibleCompanyId), Company(hiddenCompanyId)];
        CompanyReadStore companies = new(values);
        RestaurantReadStore restaurants = new(
            Restaurant(visibleRestaurantId, visibleCompanyId),
            Restaurant(hiddenRestaurantId, hiddenCompanyId)
        );
        ClaimsPrincipal user = new(
            new ClaimsIdentity(
                [
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(
                            visibleRestaurantId,
                            RestaurantPermissions.BackofficeAccess
                        )
                    ),
                ],
                "test"
            )
        );
        CompaniesReadController controller = CreateController(companies, restaurants, user);

        ActionResult<IReadOnlyList<CompanyResponse>> result = await controller.List(
            CancellationToken.None
        );

        CompanyResponse[] visibleCompanies = Assert.IsType<CompanyResponse[]>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
        CompanyResponse visible = Assert.Single(visibleCompanies);
        Assert.Equal(visibleCompanyId, visible.Id);
        Assert.Equal(1, companies.ReadCount);
        Assert.Equal(1, restaurants.ReadCount);
    }

    private static CompaniesReadController CreateController(
        CompanyReadStore companies,
        RestaurantReadStore restaurants,
        ClaimsPrincipal user
    ) =>
        new(companies, restaurants)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };

    private static CompanyResponse Company(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Empresa", "B123", "Calle 1", null, null, 1, DateTime.UtcNow);

    private static RestaurantResponse Restaurant(Guid id, Guid companyId) =>
        new(id, "NORTE", "Local", "Calle 1", companyId, true, 1, DateTime.UtcNow);

    private sealed class CompanyReadStore(params CompanyResponse[] values) : ICompanyReadStore
    {
        public int ReadCount { get; private set; }

        public Task<CompanyResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(values.FirstOrDefault(company => company.Id == id));

        public Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<CompanyResponse>>(values);
        }
    }

    private sealed class RestaurantReadStore(params RestaurantResponse[] values)
        : IRestaurantReadStore
    {
        public int ReadCount { get; private set; }

        public Task<RestaurantResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(values.FirstOrDefault(restaurant => restaurant.Id == id));

        public Task<IReadOnlyList<RestaurantResponse>> ListAsync(
            CancellationToken cancellationToken
        )
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<RestaurantResponse>>(values);
        }
    }
}
