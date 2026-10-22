using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class RestaurantSecurityAuthorizationTests
{
    public static TheoryData<string, string[]> RolePermissionMatrix =>
        new()
        {
            {
                "Admin",
                [
                    RestaurantPermissions.BackofficeAccess,
                    RestaurantPermissions.PosUse,
                    RestaurantPermissions.CommanderUse,
                    RestaurantPermissions.IdentityManage,
                    RestaurantPermissions.TablesRead,
                    RestaurantPermissions.TablesManage,
                    RestaurantPermissions.TablesRelease,
                    RestaurantPermissions.OrdersCreate,
                    RestaurantPermissions.OrdersRead,
                    RestaurantPermissions.OrdersReportRead,
                    RestaurantPermissions.OrdersManage,
                    RestaurantPermissions.OrdersRecover,
                    RestaurantPermissions.PaymentsCapture,
                    RestaurantPermissions.PaymentsRefund,
                    RestaurantPermissions.CashRegisterManage,
                    RestaurantPermissions.CashRegisterRead,
                    RestaurantPermissions.CatalogManage,
                    RestaurantPermissions.CatalogGlobalManage,
                    RestaurantPermissions.KdsUse,
                    RestaurantPermissions.DashboardRead,
                    RestaurantPermissions.FinancialReportsRead,
                    RestaurantPermissions.MarketingReportsRead,
                    RestaurantPermissions.KitchenReportsRead,
                    RestaurantPermissions.DiscountsApply,
                ]
            },
            {
                "Gerente",
                [
                    RestaurantPermissions.BackofficeAccess,
                    RestaurantPermissions.OrdersRead,
                    RestaurantPermissions.OrdersReportRead,
                    RestaurantPermissions.DashboardRead,
                    RestaurantPermissions.CashRegisterRead,
                    RestaurantPermissions.FinancialReportsRead,
                    RestaurantPermissions.PaymentsRefund,
                    RestaurantPermissions.MarketingReportsRead,
                    RestaurantPermissions.KitchenReportsRead,
                ]
            },
            {
                "Contabilidad",
                [
                    RestaurantPermissions.BackofficeAccess,
                    RestaurantPermissions.OrdersRead,
                    RestaurantPermissions.OrdersReportRead,
                    RestaurantPermissions.DashboardRead,
                    RestaurantPermissions.CashRegisterRead,
                    RestaurantPermissions.FinancialReportsRead,
                    RestaurantPermissions.PaymentsRefund,
                ]
            },
            {
                "Marketing",
                [
                    RestaurantPermissions.BackofficeAccess,
                    RestaurantPermissions.OrdersReportRead,
                    RestaurantPermissions.DashboardRead,
                    RestaurantPermissions.FinancialReportsRead,
                    RestaurantPermissions.MarketingReportsRead,
                ]
            },
            {
                "Manager",
                [
                    RestaurantPermissions.BackofficeAccess,
                    RestaurantPermissions.PosUse,
                    RestaurantPermissions.TablesRead,
                    RestaurantPermissions.TablesManage,
                    RestaurantPermissions.TablesRelease,
                    RestaurantPermissions.OrdersCreate,
                    RestaurantPermissions.OrdersRead,
                    RestaurantPermissions.OrdersReportRead,
                    RestaurantPermissions.OrdersManage,
                    RestaurantPermissions.OrdersRecover,
                    RestaurantPermissions.PaymentsCapture,
                    RestaurantPermissions.PaymentsRefund,
                    RestaurantPermissions.CashRegisterManage,
                    RestaurantPermissions.CatalogManage,
                    RestaurantPermissions.KitchenReportsRead,
                    RestaurantPermissions.DiscountsApply,
                ]
            },
            {
                "Camarero",
                [
                    RestaurantPermissions.CommanderUse,
                    RestaurantPermissions.TablesRead,
                    RestaurantPermissions.TablesReleasePaid,
                    RestaurantPermissions.OrdersCreate,
                    RestaurantPermissions.OrdersManage,
                    RestaurantPermissions.PaymentsCaptureCard,
                ]
            },
            { "Kds", [RestaurantPermissions.KdsUse] },
            { "Unknown", [] },
        };

    [Fact]
    public void Security_registration_requires_configuration_and_a_32_byte_signing_key()
    {
        IServiceCollection missingConfiguration = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            missingConfiguration.AddRestaurantSecurity(new ConfigurationBuilder().Build())
        );

        IServiceCollection shortKey = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Security:SigningKey"] = "too-short" }
            )
            .Build();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            shortKey.AddRestaurantSecurity(configuration)
        );

        Assert.Contains("at least 32 bytes", exception.Message);
    }

    [Fact]
    public async Task Security_registration_configures_jwt_validation_and_hub_query_token()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Security:Issuer"] = "test-issuer",
                    ["Security:Audience"] = "test-audience",
                    ["Security:SigningKey"] = "0123456789abcdef0123456789abcdef",
                    ["Security:AccessTokenMinutes"] = "60",
                }
            )
            .Build();

        services.AddRestaurantSecurity(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        JwtBearerOptions options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("test-issuer", options.TokenValidationParameters.ValidIssuer);
        Assert.Equal("test-audience", options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);

        DefaultHttpContext hubContext = new();
        hubContext.Request.Path = "/hubs/kds";
        hubContext.Request.QueryString = new QueryString("?access_token=hub-token");
        MessageReceivedContext hubMessage = new(
            hubContext,
            new AuthenticationScheme(
                JwtBearerDefaults.AuthenticationScheme,
                JwtBearerDefaults.AuthenticationScheme,
                typeof(JwtBearerHandler)
            ),
            options
        );
        await options.Events.OnMessageReceived(hubMessage);
        Assert.Equal("hub-token", hubMessage.Token);

        DefaultHttpContext ordinaryContext = new();
        ordinaryContext.Request.Path = "/api/orders";
        ordinaryContext.Request.QueryString = new QueryString("?access_token=query-token");
        MessageReceivedContext ordinaryMessage = new(
            ordinaryContext,
            new AuthenticationScheme(
                JwtBearerDefaults.AuthenticationScheme,
                JwtBearerDefaults.AuthenticationScheme,
                typeof(JwtBearerHandler)
            ),
            options
        );
        await options.Events.OnMessageReceived(ordinaryMessage);
        Assert.Null(ordinaryMessage.Token);
    }

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

    [Theory]
    [MemberData(nameof(RolePermissionMatrix))]
    public void Role_permission_matrix_matches_the_access_policy(string role, string[] expected)
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal user = Principal(new Claim(ClaimTypes.Role, role));

        Assert.Equal(expected, RestaurantPermissions.ForRole(role));
        Assert.All(
            expected,
            permission => Assert.True(user.CanAccessRestaurant(restaurantId, permission))
        );
        Assert.False(user.CanAccessRestaurant(restaurantId, "permission.outside.role.matrix"));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
