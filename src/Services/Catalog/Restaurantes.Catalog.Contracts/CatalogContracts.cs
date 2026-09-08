using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts;

public sealed class CreateCategoryRequest
{
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; init; } = "";

    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string DefaultStationCode { get; init; } = "GENERAL";

    [Required, StringLength(80)]
    public string DefaultStationName { get; init; } = "General";
}

public sealed class UpdateCategoryRequest
{
    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string DefaultStationCode { get; init; } = "GENERAL";

    [Required, StringLength(80)]
    public string DefaultStationName { get; init; } = "General";
    public bool IsActive { get; init; } = true;
}

public sealed class CreateProductRequest
{
    [Required, StringLength(60), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Sku { get; init; } = "";

    [Required, StringLength(160)]
    public string Name { get; init; } = "";
    public Guid CategoryId { get; init; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal BasePrice { get; init; }
}

public sealed class UpdateProductRequest
{
    [Required, StringLength(160)]
    public string Name { get; init; } = "";
    public Guid CategoryId { get; init; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal BasePrice { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class ConfigureMenuItemRequest
{
    [Range(typeof(decimal), "0", "9999999")]
    public decimal Price { get; init; }
    public bool IsAvailable { get; init; } = true;

    [StringLength(40), RegularExpression("^[A-Za-z0-9_-]*$")]
    public string? PreparationStationCode { get; init; }

    [StringLength(80)]
    public string? PreparationStationName { get; init; }
}

public sealed class CreateKitchenStationRequest
{
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; init; } = "";

    [Required, StringLength(80)]
    public string Name { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool RequiresPrimaryDispatch { get; init; }

    [Range(0, 99)]
    public int Priority { get; init; } = 1;
}

public sealed class UpdateKitchenStationRequest
{
    [Required, StringLength(80)]
    public string Name { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public bool IsPrimary { get; init; }
    public bool RequiresPrimaryDispatch { get; init; }

    [Range(0, 99)]
    public int Priority { get; init; } = 1;
}

public sealed record CategoryResponse(
    Guid Id,
    string Code,
    string Name,
    string DefaultStationCode,
    string DefaultStationName,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    decimal BasePrice,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);

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
    int Version,
    DateTime UpdatedAtUtc
);

public sealed record KitchenStationResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Name,
    bool IsPrimary,
    bool RequiresPrimaryDispatch,
    int Priority,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);

public sealed record CategoryChanged(
    Guid CategoryId,
    string Code,
    string Name,
    string DefaultStationCode,
    string DefaultStationName,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);

public sealed record ProductChanged(
    Guid ProductId,
    string Sku,
    string Name,
    Guid CategoryId,
    decimal BasePrice,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);

public sealed record CatalogItemChanged(
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
    int Version,
    DateTime OccurredAtUtc
);

public sealed record KitchenStationChanged(
    Guid StationId,
    Guid RestaurantId,
    string Code,
    string Name,
    bool IsPrimary,
    bool RequiresPrimaryDispatch,
    int Priority,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
