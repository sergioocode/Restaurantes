namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record MenuItemResponse(
    Guid RestaurantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    decimal Price,
    bool IsAvailable,
    string PreparationStationCode,
    string PreparationStationName,
    IReadOnlyList<ProductExtraGroup>? ExtraGroups = null
);

public sealed record ProductExtraGroup(
    Guid Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<ProductExtraOption> Options
);

public sealed record ProductExtraOption(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Price
);

public sealed class CommanderCartLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductId { get; init; }
    public int Quantity { get; set; } = 1;
    public List<CommanderCartExtra> Extras { get; init; } = [];
}

public sealed record CommanderCartExtra(Guid GroupId, Guid ProductId, int Quantity);
