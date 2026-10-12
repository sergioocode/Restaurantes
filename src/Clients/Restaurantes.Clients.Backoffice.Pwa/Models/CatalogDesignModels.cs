namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CatalogDesignSnapshot
{
    public List<ProductResponse> Products { get; set; } = [];
    public List<CatalogTaxModel> Taxes { get; set; } = [];
    public List<CatalogAllergenModel> Allergens { get; set; } = [];
    public List<ProductTaxModel> ProductTaxes { get; set; } = [];
    public List<ProductAllergenModel> ProductAllergens { get; set; } = [];
    public List<ProductPhotoModel> ProductPhotos { get; set; } = [];
    public List<ProductConfigurationModel> Configurations { get; set; } = [];
    public List<ProductOptionGroupModel> Groups { get; set; } = [];
    public List<ProductOptionModel> Options { get; set; } = [];
    public List<CatalogPriceModel> Prices { get; set; } = [];
    public List<RestaurantProductAvailabilityModel> LocalAvailability { get; set; } = [];
}

public sealed class RestaurantProductAvailabilityModel
{
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class CatalogTaxModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CatalogAllergenModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class ProductTaxModel
{
    public Guid ProductId { get; set; }
    public Guid TaxId { get; set; }
}

public sealed class ProductAllergenModel
{
    public Guid ProductId { get; set; }
    public Guid AllergenId { get; set; }
}

public sealed class ProductPhotoModel
{
    public Guid ProductId { get; set; }
    public Guid Revision { get; set; }
    public string OriginalContentType { get; set; } = "";
}

public sealed class ProductConfigurationModel
{
    public Guid ProductId { get; set; }
    public bool IsCombo { get; set; }
    public bool AllowsExtras { get; set; }
}

public sealed class ProductOptionGroupModel
{
    public Guid Id { get; set; }
    public Guid ParentProductId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Extra";
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public int SortOrder { get; set; }
}

public sealed class ProductOptionModel
{
    public Guid GroupId { get; set; }
    public Guid ProductId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CatalogPriceModel
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Channel { get; set; } = "LOCAL";
    public string Format { get; set; } = "UNIDAD";
    public decimal Amount { get; set; }
}

public sealed class ProductDefinitionDraft
{
    public bool IsCombo { get; set; }
    public bool AllowsExtras { get; set; }
    public List<OptionGroupDraft> Groups { get; set; } = [];
}

public sealed class OptionGroupDraft
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Extra";
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public List<Guid> ProductIds { get; set; } = [];
}

public sealed class PriceDraft
{
    public string Channel { get; set; } = "LOCAL";
    public string Format { get; set; } = "UNIDAD";
    public decimal Amount { get; set; }
}
