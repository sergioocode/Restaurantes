namespace Restaurantes.Catalog.Domain;

public sealed class Product
{
    private Product() { }

    private Product(string sku, string name, Guid categoryId, DateTime now)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("CategoryId is required.");
        }

        Id = Guid.NewGuid();
        Sku = SkuValue(sku);
        Name = Category.Required(name, 160);
        CategoryId = categoryId;
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid CategoryId { get; private set; }
    public Guid? TaxId { get; private set; }
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Product Create(string sku, string name, Guid categoryId, DateTime now) =>
        new(sku, name, categoryId, now);

    public void Update(string name, Guid categoryId, bool isActive, DateTime now)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("CategoryId is required.");
        }

        Name = Category.Required(name, 160);
        CategoryId = categoryId;
        IsActive = isActive;
        Version++;
        UpdatedAtUtc = now;
    }

    public void AssignTax(Guid? taxId) => TaxId = taxId;

    private static string SkuValue(string value)
    {
        string result = Category.Required(value, 60).ToUpperInvariant();
        return !result.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ':')
            ? throw new ArgumentException(
                "SKU may only contain letters, numbers, '-', '_' and ':'."
            )
            : result;
    }
}
