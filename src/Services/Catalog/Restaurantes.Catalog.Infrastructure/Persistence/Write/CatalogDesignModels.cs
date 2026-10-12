namespace Restaurantes.Catalog.Infrastructure.Persistence.Write;

public sealed class CatalogTax
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CatalogAllergen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class ProductAllergen
{
    public Guid ProductId { get; set; }
    public Guid AllergenId { get; set; }
}

public sealed class ProductPhoto
{
    public Guid ProductId { get; set; }
    public Guid Revision { get; set; }
    public string OriginalContentType { get; set; } = "image/jpeg";
}

public sealed class ProductConfiguration
{
    public Guid ProductId { get; set; }
    public bool IsCombo { get; set; }
    public bool AllowsExtras { get; set; }
}

public sealed class ProductOptionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentProductId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Extra";
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public int SortOrder { get; set; }
}

public sealed class ProductOption
{
    public Guid GroupId { get; set; }
    public Guid ProductId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CatalogPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Channel { get; set; } = "LOCAL";
    public string Format { get; set; } = "UNIDAD";
    public decimal Amount { get; set; }
}

public sealed class CatalogMenu
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = "Carta general";
    public bool IsActive { get; set; }
}

public sealed class RestaurantProductAvailability
{
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public sealed class CatalogMenuEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MenuId { get; set; }
    public Guid ProductId { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string DefaultFormat { get; set; } = "PLATO";
    public int SortOrder { get; set; }
}

public sealed class CatalogMenuPrice
{
    public Guid MenuId { get; set; }
    public Guid ProductId { get; set; }
    public string Channel { get; set; } = "LOCAL";
    public string Format { get; set; } = "PLATO";
    public decimal Amount { get; set; }
}

public sealed class CatalogMenuChannel
{
    public Guid MenuEntryId { get; set; }
    public string Channel { get; set; } = "GLOVO";
    public bool IsPublished { get; set; }
    public string ExternalId { get; set; } = "";
}
