using System.Security.Claims;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class RestaurantSecurityAuthorizationTests
{
    [Fact]
    public void Restaurant_access_requires_an_authenticated_user_and_a_restaurant_id()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal anonymous = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));
        ClaimsPrincipal admin = Principal(new Claim(ClaimTypes.Role, "Admin"));
        ClaimsPrincipal unknownRole = Principal(new Claim(ClaimTypes.Role, "Unknown"));

        Assert.False(
            anonymous.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage)
        );
        Assert.False(
            admin.CanAccessRestaurant(Guid.Empty, RestaurantPermissions.CashRegisterManage)
        );
        Assert.True(
            admin.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage)
        );
        Assert.False(
            unknownRole.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage)
        );
    }

    [Fact]
    public void Scoped_permissions_are_limited_to_the_named_restaurant()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal user = Principal(
            new Claim(
                RestaurantClaimTypes.Permission,
                RestaurantSecurityExtensions
                    .ScopedPermission(restaurantId, RestaurantPermissions.CashRegisterRead)
                    .ToUpperInvariant()
            )
        );

        Assert.Equal(
            $"{restaurantId:N}:{RestaurantPermissions.CashRegisterRead}",
            RestaurantSecurityExtensions.ScopedPermission(
                restaurantId,
                RestaurantPermissions.CashRegisterRead
            )
        );
        Assert.True(user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterRead));
        Assert.False(
            user.CanAccessRestaurant(Guid.NewGuid(), RestaurantPermissions.CashRegisterRead)
        );
    }

    [Fact]
    public void Dashboard_read_uses_the_scoped_role_permissions()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal accountant = Principal(
            new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Contabilidad")
        );
        ClaimsPrincipal kitchenDisplay = Principal(
            new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Kds")
        );

        Assert.True(
            accountant.CanAccessRestaurant(restaurantId, RestaurantPermissions.DashboardRead)
        );
        Assert.False(
            accountant.CanAccessRestaurant(Guid.NewGuid(), RestaurantPermissions.DashboardRead)
        );
        Assert.False(
            kitchenDisplay.CanAccessRestaurant(restaurantId, RestaurantPermissions.DashboardRead)
        );
    }

    [Fact]
    public void Has_any_permission_supports_global_roles_scoped_dashboard_roles_and_claims()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal admin = Principal(new Claim(ClaimTypes.Role, "Admin"));
        ClaimsPrincipal accountant = Principal(
            new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Contabilidad")
        );
        ClaimsPrincipal scopedPermission = Principal(
            new Claim(
                RestaurantClaimTypes.Permission,
                RestaurantSecurityExtensions.ScopedPermission(
                    restaurantId,
                    RestaurantPermissions.CashRegisterRead
                )
            )
        );
        ClaimsPrincipal invalidScopedRole = Principal(
            new Claim(RestaurantClaimTypes.RestaurantRole, "not-a-guid:Contabilidad")
        );

        Assert.True(admin.HasAnyRestaurantPermission(RestaurantPermissions.CashRegisterManage));
        Assert.True(accountant.HasAnyRestaurantPermission(RestaurantPermissions.DashboardRead));
        Assert.True(
            scopedPermission.HasAnyRestaurantPermission(RestaurantPermissions.CashRegisterRead)
        );
        Assert.False(
            invalidScopedRole.HasAnyRestaurantPermission(RestaurantPermissions.DashboardRead)
        );
        Assert.False(
            new ClaimsPrincipal().HasAnyRestaurantPermission(RestaurantPermissions.CashRegisterRead)
        );
        Assert.Empty(RestaurantPermissions.ForRole("Unknown"));
    }

    [Theory]
    [InlineData(
        "Gerente",
        RestaurantPermissions.CashRegisterRead,
        RestaurantPermissions.CashRegisterManage
    )]
    [InlineData(
        "Contabilidad",
        RestaurantPermissions.FinancialReportsRead,
        RestaurantPermissions.CashRegisterManage
    )]
    [InlineData(
        "Marketing",
        RestaurantPermissions.MarketingReportsRead,
        RestaurantPermissions.CashRegisterManage
    )]
    [InlineData(
        "Manager",
        RestaurantPermissions.CashRegisterManage,
        RestaurantPermissions.IdentityManage
    )]
    [InlineData(
        "Camarero",
        RestaurantPermissions.PaymentsCaptureCard,
        RestaurantPermissions.CashRegisterManage
    )]
    [InlineData("Kds", RestaurantPermissions.KdsUse, RestaurantPermissions.CashRegisterRead)]
    public void Roles_grant_their_operational_permission_without_cash_or_identity_escalation(
        string role,
        string granted,
        string denied
    )
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal user = Principal(new Claim(ClaimTypes.Role, role));

        Assert.Contains(granted, RestaurantPermissions.ForRole(role));
        Assert.True(user.CanAccessRestaurant(restaurantId, granted));
        Assert.DoesNotContain(denied, RestaurantPermissions.ForRole(role));
        Assert.False(user.CanAccessRestaurant(restaurantId, denied));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
