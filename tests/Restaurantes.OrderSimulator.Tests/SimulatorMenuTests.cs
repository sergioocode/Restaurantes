using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorMenuTests
{
    private static readonly Guid RestaurantId = Guid.Parse("20893727-37e0-4e9b-af61-a616b61f448a");

    [Fact]
    public void Configuration_path_uses_default_or_explicit_file_and_rejects_invalid_arguments()
    {
        Assert.Equal(
            Path.Combine("tools", "order-simulator", "simulator.local.json"),
            Simulator.ReadConfigPath([])
        );
        Assert.Equal("local.json", Simulator.ReadConfigPath(["--config", "local.json"]));
        Assert.Throws<ArgumentException>(() => Simulator.ReadConfigPath(["unexpected"]));
    }

    [Fact]
    public void Available_menu_excludes_extras_and_products_without_required_options()
    {
        Guid mainId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        Guid unavailableId = Guid.NewGuid();
        Guid blockedId = Guid.NewGuid();
        ProductExtraGroup availableGroup = Group(extraId);
        ProductExtraGroup unavailableGroup = Group(unavailableId);
        MenuItemResponse[] menu =
        [
            Product(mainId, groups: [availableGroup]),
            Product(extraId, categoryName: "Extras"),
            Product(unavailableId, isAvailable: false),
            Product(blockedId, groups: [unavailableGroup]),
        ];

        MenuItemResponse selected = Assert.Single(Simulator.FilterUsableMenu(menu));

        Assert.Equal(mainId, selected.ProductId);
    }

    [Fact]
    public void Created_order_line_contains_a_required_available_extra()
    {
        Guid mainId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        ProductExtraGroup group = Group(extraId);
        RestaurantOptions restaurant = new() { MaxProductsPerOrder = 1 };
        using SemaphoreSlim randomLock = new(1, 1);

        var line = Assert.Single(
            Simulator.CreateRandomLines(
                restaurant,
                [Product(mainId, groups: [group])],
                new HashSet<Guid> { mainId, extraId },
                new Simulator.SimulatorRandom(17),
                randomLock
            )
        );

        Assert.Equal(mainId, line.ProductId);
        Assert.InRange(line.Quantity, 1, 3);
        Assert.Equal(extraId, Assert.Single(line.SelectedExtras).ProductId);
    }

    [Fact]
    public void Required_extra_without_available_options_is_rejected()
    {
        Guid mainId = Guid.NewGuid();
        ProductExtraGroup group = Group(Guid.NewGuid());
        using SemaphoreSlim randomLock = new(1, 1);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            Simulator.CreateRandomLines(
                new RestaurantOptions { MaxProductsPerOrder = 1 },
                [Product(mainId, groups: [group])],
                new HashSet<Guid> { mainId },
                new Simulator.SimulatorRandom(17),
                randomLock
            )
        );

        Assert.Contains("no tiene suficientes opciones disponibles", error.Message);
    }

    [Fact]
    public void Tables_from_different_zones_are_interleaved_without_losing_them()
    {
        TableInfo[] tables =
        [
            Table("A1", "A"),
            Table("A2", "A"),
            Table("B1", "B"),
            Table("B2", "B"),
        ];
        using SemaphoreSlim randomLock = new(1, 1);
        Simulator.SimulatorRandom random = new(17);

        List<TableInfo> result = Simulator.InterleaveTablesByZone(tables, random, randomLock);

        Assert.Equal(
            tables.Select(table => table.Id).Order(),
            result.Select(table => table.Id).Order()
        );
        for (int index = 1; index < result.Count; index++)
        {
            Assert.NotEqual(result[index - 1].ZoneName, result[index].ZoneName);
        }
        Assert.Empty(Simulator.InterleaveTablesByZone([], random, randomLock));
    }

    private static TableInfo Table(string label, string zone) =>
        new(Guid.NewGuid(), label, "Available", true, null, false, zone);

    private static ProductExtraGroup Group(Guid extraId) =>
        new(
            Guid.NewGuid(),
            "Guarnición",
            1,
            1,
            [new ProductExtraOption(extraId, "EXTRA", "Extra", 1m)]
        );

    private static MenuItemResponse Product(
        Guid id,
        bool isAvailable = true,
        string categoryName = "Platos",
        IReadOnlyList<ProductExtraGroup>? groups = null
    ) =>
        new(
            RestaurantId,
            id,
            "SKU",
            "Producto",
            Guid.NewGuid(),
            "PLATOS",
            categoryName,
            10m,
            isAvailable,
            "CHEF",
            "Chef",
            1,
            DateTime.UnixEpoch,
            ExtraGroups: groups
        );
}
