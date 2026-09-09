using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Restaurantes.Security;

public sealed class RestaurantSecurityOptions
{
    public const string SectionName = "Security";
    public string Issuer { get; init; } = "Restaurantes.Identity";
    public string Audience { get; init; } = "Restaurantes";
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 480;
}

public static class RestaurantClaimTypes
{
    public const string RestaurantId = "restaurant_id";
    public const string RestaurantRole = "restaurant_role";
    public const string Permission = "restaurant_permission";
}

public static class RestaurantPermissions
{
    public const string BackofficeAccess = "backoffice.access";
    public const string IdentityManage = "identity.manage";
    public const string TablesRead = "tables.read";
    public const string TablesManage = "tables.manage";
    public const string TablesRelease = "tables.release";
    public const string OrdersCreate = "orders.create";
    public const string OrdersManage = "orders.manage";
    public const string OrdersRecover = "orders.recover";
    public const string PaymentsCapture = "payments.capture";
    public const string PaymentsRefund = "payments.refund";
    public const string CashRegisterManage = "cash-register.manage";
    public const string CatalogManage = "catalog.manage";
    public const string CatalogGlobalManage = "catalog.global.manage";
    public const string KdsUse = "kds.use";
    public const string DashboardRead = "dashboard.read";
    public const string FinancialReportsRead = "reports.financial.read";
    public const string MarketingReportsRead = "reports.marketing.read";
    public const string KitchenReportsRead = "reports.kitchen.read";

    public static IReadOnlyCollection<string> ForRole(string role)
    {
        return role switch
        {
            "Admin" =>
            [
                BackofficeAccess,
                IdentityManage,
                TablesRead,
                TablesManage,
                TablesRelease,
                OrdersCreate,
                OrdersManage,
                OrdersRecover,
                PaymentsCapture,
                PaymentsRefund,
                CashRegisterManage,
                CatalogManage,
                CatalogGlobalManage,
                KdsUse,
                DashboardRead,
                FinancialReportsRead,
                MarketingReportsRead,
                KitchenReportsRead,
            ],
            "Gerente" => [BackofficeAccess, DashboardRead, FinancialReportsRead, PaymentsRefund],
            "Contabilidad" =>
            [
                BackofficeAccess,
                DashboardRead,
                FinancialReportsRead,
                PaymentsRefund,
            ],
            "Oficina" =>
            [
                BackofficeAccess,
                DashboardRead,
                FinancialReportsRead,
                MarketingReportsRead,
            ],
            "Manager" =>
            [
                BackofficeAccess,
                TablesRead,
                TablesManage,
                TablesRelease,
                OrdersCreate,
                OrdersManage,
                OrdersRecover,
                PaymentsCapture,
                CashRegisterManage,
                CatalogManage,
                KdsUse,
                KitchenReportsRead,
            ],
            "PosComandero" =>
            [
                BackofficeAccess,
                TablesRead,
                TablesRelease,
                OrdersCreate,
                OrdersManage,
                PaymentsCapture,
                CashRegisterManage,
            ],
            "Kds" => [KdsUse],
            _ => [],
        };
    }
}

public static class RestaurantSecurityExtensions
{
    public static IServiceCollection AddRestaurantSecurity(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        RestaurantSecurityOptions options =
            configuration
                .GetSection(RestaurantSecurityOptions.SectionName)
                .Get<RestaurantSecurityOptions>()
            ?? throw new InvalidOperationException("Security configuration is missing.");

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Security:SigningKey must contain at least 32 bytes."
            );
        }

        services.Configure<RestaurantSecurityOptions>(
            configuration.GetSection(RestaurantSecurityOptions.SectionName)
        );
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(options.SigningKey)
                    ),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        string? accessToken = context
                            .Request.Query["access_token"]
                            .FirstOrDefault();
                        if (
                            !string.IsNullOrWhiteSpace(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                        )
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization();
        return services;
    }

    public static bool CanAccessRestaurant(
        this ClaimsPrincipal user,
        Guid restaurantId,
        string permission
    )
    {
        if (user.Identity?.IsAuthenticated != true || restaurantId == Guid.Empty)
        {
            return false;
        }

        if (user.IsInRole("Admin"))
        {
            return true;
        }

        foreach (string globalRole in new[] { "Gerente", "Contabilidad", "Oficina" })
        {
            if (
                user.IsInRole(globalRole)
                && RestaurantPermissions.ForRole(globalRole).Contains(permission)
            )
            {
                return true;
            }
        }

        string expected = $"{restaurantId:N}:{permission}";
        return user.Claims.Any(claim =>
            claim.Type == RestaurantClaimTypes.Permission
            && string.Equals(claim.Value, expected, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static string ScopedPermission(Guid restaurantId, string permission)
    {
        return $"{restaurantId:N}:{permission}";
    }

    public static bool HasAnyRestaurantPermission(this ClaimsPrincipal user, string permission)
    {
        if (user.IsInRole("Admin"))
        {
            return true;
        }

        string suffix = $":{permission}";
        return user.Identity?.IsAuthenticated == true
            && user.Claims.Any(claim =>
                claim.Type == RestaurantClaimTypes.Permission
                && claim.Value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            );
    }
}
