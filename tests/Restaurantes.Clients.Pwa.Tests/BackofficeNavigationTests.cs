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

    [Theory]
    [InlineData("tables", "Mesas", "OPERACIÓN DEL LOCAL")]
    [InlineData("zones", "Zonas del restaurante", "OPERACIÓN DEL LOCAL")]
    [InlineData("restaurants", "Locales", "CADENA")]
    [InlineData("companies", "Empresas", "CADENA")]
    [InlineData("stations", "Estaciones Kitchen Display System", "CATÁLOGO DEL LOCAL")]
    [InlineData("orders", "Pedidos", "OPERACIÓN DEL LOCAL")]
    [InlineData("daily-orders-report", "Pedidos por día", "INFORMES")]
    [InlineData("cash-registers-report", "Cajas por día", "INFORMES")]
    [InlineData("categories", "Categorías", "CATÁLOGO GLOBAL")]
    [InlineData("products", "Carta Global", "CATÁLOGO GLOBAL")]
    [InlineData("taxes", "Impuestos", "CATÁLOGO GLOBAL")]
    [InlineData("allergens", "Alérgenos", "CATÁLOGO GLOBAL")]
    [InlineData("catalog-design", "Carta del local", "CATÁLOGO DEL LOCAL")]
    [InlineData("users", "Usuarios, roles y locales", "SEGURIDAD")]
    [InlineData("other", "Backoffice", "CATÁLOGO DEL LOCAL")]
    public void Module_heading_matches_the_selected_module(
        string module,
        string expectedTitle,
        string expectedEyebrow
    )
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome home = Home(
            restaurantId,
            [new(restaurantId, "Gerente", ["backoffice.access", "tables.manage"])],
            [restaurantId],
            ["Admin"],
            ["backoffice.access", "catalog.global.manage", "identity.manage"]
        );
        SetField(home, "module", module);

        Assert.Equal(expectedTitle, GetProperty<string>(home, "ModuleTitle"));
        Assert.Equal(expectedEyebrow, GetProperty<string>(home, "ModuleEyebrow"));
    }

    [Fact]
    public void Refresh_action_labels_follow_module_permissions()
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome admin = Home(
            restaurantId,
            [],
            [restaurantId],
            ["Admin"],
            ["catalog.global.manage", "identity.manage"]
        );
        (string Module, string Expected)[] adminLabels =
        [
            ("products", "Recargar"),
            ("categories", "Actualizar categorías"),
            ("taxes", "Actualizar impuestos"),
            ("allergens", "Actualizar alérgenos"),
            ("users", "Actualizar usuarios"),
        ];
        foreach ((string module, string expected) in adminLabels)
        {
            SetField(admin, "module", module);
            Assert.Equal(expected, GetProperty<string?>(admin, "RefreshLabel"));
        }

        SetField(admin, "module", "tables");
        Assert.Null(GetProperty<string?>(admin, "RefreshLabel"));

        BackofficeHome manager = Home(
            restaurantId,
            [
                new(
                    restaurantId,
                    "Manager",
                    [
                        "backoffice.access",
                        "catalog.manage",
                        "tables.manage",
                        "kds.use",
                        "orders.read",
                        "orders.report.read",
                        "cash-register.read",
                    ]
                ),
            ],
            [restaurantId]
        );
        (string Module, string Expected)[] managerLabels =
        [
            ("catalog-design", "Recargar"),
            ("orders", "Consultar pedidos"),
            ("daily-orders-report", "Consultar pedidos"),
            ("cash-registers-report", "Consultar cajas"),
            ("stations", "Actualizar estaciones"),
            ("tables", "Actualizar mesas"),
            ("zones", "Actualizar zonas"),
        ];
        foreach ((string module, string expected) in managerLabels)
        {
            SetField(manager, "module", module);
            Assert.Equal(expected, GetProperty<string?>(manager, "RefreshLabel"));
        }

        BackofficeHome chainManager = Home(
            restaurantId,
            [new(restaurantId, "Gerente", ["backoffice.access", "tables.manage"])],
            [restaurantId]
        );
        foreach (string module in new[] { "companies", "restaurants" })
        {
            SetField(chainManager, "module", module);
            Assert.StartsWith("Actualizar", GetProperty<string?>(chainManager, "RefreshLabel"));
        }
    }

    [Fact]
    public void Report_context_selector_respects_scope_and_role()
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome marketing = Home(
            restaurantId,
            [],
            [restaurantId],
            ["Marketing"],
            ["orders.report.read"]
        );
        SetField(marketing, "module", "daily-orders-report");
        Assert.True(GetProperty<bool>(marketing, "ShowRestaurantContextSelector"));
        SetField(marketing, "module", "cash-registers-report");
        Assert.False(GetProperty<bool>(marketing, "ShowRestaurantContextSelector"));

        BackofficeHome admin = Home(
            restaurantId,
            [],
            [restaurantId],
            ["Admin"],
            ["backoffice.access"]
        );
        foreach (string module in new[] { "users", "categories", "tables", "zones" })
        {
            SetField(admin, "module", module);
            Assert.True(GetProperty<bool>(admin, "ShowRestaurantContextSelector"));
        }
        SetField(admin, "module", "products");
        Assert.False(GetProperty<bool>(admin, "ShowRestaurantContextSelector"));
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

    private static T GetProperty<T>(object component, string name) =>
        (T)
            component
                .GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(component)!;
}
