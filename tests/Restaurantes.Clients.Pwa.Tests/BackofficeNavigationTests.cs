using System.Collections;
using System.Reflection;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeNavigationTests
{
    [Fact]
    public void Local_permissions_show_only_authorized_locations_and_reports()
    {
        Guid allowedId = Guid.NewGuid();
        Guid forbiddenId = Guid.NewGuid();
        BackofficeHome home = Home(
            allowedId,
            [
                new(
                    allowedId,
                    "Camarero",
                    ["backoffice.access", "tables.read", "orders.report.read"]
                ),
                new(forbiddenId, "Camarero", ["catalog.manage"]),
            ],
            [allowedId, forbiddenId]
        );

        string[] nodes = NavigationIds(home);

        Assert.Contains("tables", nodes);
        Assert.Contains("zones", nodes);
        Assert.Contains("daily-orders-report", nodes);
        Assert.DoesNotContain("cash-registers-report", nodes);
        Assert.DoesNotContain("catalog-design", nodes);
        Assert.DoesNotContain("users", nodes);
        Assert.DoesNotContain("group-management", nodes);
    }

    [Fact]
    public void Manager_navigation_exposes_local_operations_without_global_configuration()
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome home = Home(
            restaurantId,
            [
                new(
                    restaurantId,
                    "Manager",
                    [
                        "backoffice.access",
                        "tables.read",
                        "catalog.manage",
                        "cash-register.read",
                        "reports.marketing.read",
                        "payments.refund",
                    ]
                ),
            ],
            [restaurantId]
        );

        string[] nodes = NavigationIds(home);

        Assert.Contains("stations", nodes);
        Assert.Contains("catalog-design", nodes);
        Assert.Contains("cash-registers-report", nodes);
        Assert.Contains("kitchen-times-report", nodes);
        Assert.Contains("refunds", nodes);
        Assert.DoesNotContain("products", nodes);
        Assert.DoesNotContain("taxes", nodes);
        Assert.DoesNotContain("users", nodes);
    }

    [Fact]
    public void Admin_navigation_contains_global_settings_and_reports()
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome home = Home(
            restaurantId,
            [],
            [restaurantId],
            ["Admin"],
            ["backoffice.access", "cash-register.manage", "orders.report.read"]
        );

        string[] nodes = NavigationIds(home);

        Assert.Contains("daily-orders-report", nodes);
        Assert.Contains("cash-registers-report", nodes);
        Assert.Contains("products", nodes);
        Assert.Contains("taxes", nodes);
        Assert.Contains("allergens", nodes);
        Assert.Contains("categories", nodes);
        Assert.Contains("companies", nodes);
        Assert.Contains("restaurants", nodes);
        Assert.Contains("users", nodes);
        Assert.DoesNotContain("stations", nodes);
    }

    private static BackofficeHome Home(
        Guid currentRestaurantId,
        List<RestaurantAccess> access,
        Guid[] restaurantIds,
        List<string>? allRoles = null,
        List<string>? allPermissions = null
    )
    {
        BackofficeHome home = new();
        SetField(home, "restaurantId", currentRestaurantId);
        SetField(
            home,
            "restaurants",
            restaurantIds
                .Select(id => new RestaurantResponse
                {
                    Id = id,
                    Name = $"Local {id:N}",
                    IsActive = true,
                })
                .ToList()
        );
        SetField(
            home,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "usuario", "Usuario"),
                access,
                allRoles,
                allPermissions
            )
        );
        return home;
    }

    private static string[] NavigationIds(BackofficeHome home)
    {
        IEnumerable nodes = (IEnumerable)
            typeof(BackofficeHome)
                .GetMethod("BuildNavigationNodes", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(home, null)!;
        return nodes
            .Cast<object>()
            .Select(node => (string)node.GetType().GetProperty("Id")!.GetValue(node)!)
            .ToArray();
    }

    private static void SetField(object component, string name, object value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
}
