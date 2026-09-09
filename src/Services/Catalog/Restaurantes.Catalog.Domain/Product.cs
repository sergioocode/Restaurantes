namespace Restaurantes.Catalog.Domain;

public sealed class Product
{
    private Product() { }

    private Product(string sku, string name, Guid categoryId, decimal basePrice, DateTime now)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("CategoryId is required.");
        }

        if (basePrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(basePrice));
        }

        Id = Guid.NewGuid();
        Sku = Category.CodeValue(sku, 60);
        Name = Category.Required(name, 160);
        CategoryId = categoryId;
        BasePrice = basePrice;
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid CategoryId { get; private set; }
    public decimal BasePrice { get; private set; }
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Product Create(
        string sku,
        string name,
        Guid categoryId,
        decimal basePrice,
        DateTime now
    )
    {
        return new(sku, name, categoryId, basePrice, now);
    }

    public void Update(string name, Guid categoryId, decimal basePrice, bool isActive, DateTime now)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("CategoryId is required.");
        }

        if (basePrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(basePrice));
        }

        Name = Category.Required(name, 160);
        CategoryId = categoryId;
        BasePrice = basePrice;
        IsActive = isActive;
        Version++;
        UpdatedAtUtc = now;
    }
}
