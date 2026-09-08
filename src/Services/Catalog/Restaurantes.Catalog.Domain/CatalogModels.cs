namespace Restaurantes.Catalog.Domain;

public sealed class Category
{
    private Category() { }

    private Category(string code, string name, string stationCode, string stationName, DateTime now)
    {
        Id = Guid.NewGuid();
        Code = CodeValue(code, 40);
        Name = Required(name, 120);
        DefaultStationCode = CodeValue(stationCode, 40);
        DefaultStationName = Required(stationName, 80);
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string DefaultStationCode { get; private set; } = "";
    public string DefaultStationName { get; private set; } = "";
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Category Create(
        string code,
        string name,
        string stationCode,
        string stationName,
        DateTime now
    )
    {
        return new(code, name, stationCode, stationName, now);
    }

    public void Update(
        string name,
        string stationCode,
        string stationName,
        bool isActive,
        DateTime now
    )
    {
        Name = Required(name, 120);
        DefaultStationCode = CodeValue(stationCode, 40);
        DefaultStationName = Required(stationName, 80);
        IsActive = isActive;
        Version++;
        UpdatedAtUtc = now;
    }

    internal static string Required(string value, int max)
    {
        string result = value.Trim();
        return result.Length is 0 || result.Length > max
            ? throw new ArgumentException($"Value must contain between 1 and {max} characters.")
            : result;
    }

    internal static string CodeValue(string value, int max)
    {
        string result = Required(value, max).ToUpperInvariant();
        return !result.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? throw new ArgumentException("Code may only contain letters, numbers, '-' and '_'.")
            : result;
    }
}

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

public sealed class RestaurantMenuItem
{
    private RestaurantMenuItem() { }

    private RestaurantMenuItem(
        Guid restaurantId,
        Guid productId,
        decimal price,
        bool available,
        string stationCode,
        string stationName,
        DateTime now
    )
    {
        Id = Guid.NewGuid();
        RestaurantId = restaurantId;
        ProductId = productId;
        Configure(price, available, stationCode, stationName, now, false);
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Price { get; private set; }
    public bool IsAvailable { get; private set; }
    public string PreparationStationCode { get; private set; } = "";
    public string PreparationStationName { get; private set; } = "";
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static RestaurantMenuItem Create(
        Guid restaurantId,
        Guid productId,
        decimal price,
        bool available,
        string stationCode,
        string stationName,
        DateTime now
    )
    {
        return restaurantId == Guid.Empty || productId == Guid.Empty
            ? throw new ArgumentException("RestaurantId and ProductId are required.")
            : new(restaurantId, productId, price, available, stationCode, stationName, now);
    }

    public void Configure(
        decimal price,
        bool available,
        string stationCode,
        string stationName,
        DateTime now
    )
    {
        Configure(price, available, stationCode, stationName, now, true);
    }

    private void Configure(
        decimal price,
        bool available,
        string stationCode,
        string stationName,
        DateTime now,
        bool increment
    )
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price));
        }

        Price = price;
        IsAvailable = available;
        PreparationStationCode = Category.CodeValue(stationCode, 40);
        PreparationStationName = Category.Required(stationName, 80);
        Version = increment ? Version + 1 : 1;
        UpdatedAtUtc = now;
    }
}

public sealed class RestaurantKitchenStation
{
    private RestaurantKitchenStation() { }

    private RestaurantKitchenStation(
        Guid restaurantId,
        string code,
        string name,
        bool isPrimary,
        bool requiresPrimaryDispatch,
        int priority,
        DateTime now
    )
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId is required.");
        }

        Id = Guid.NewGuid();
        RestaurantId = restaurantId;
        Code = Category.CodeValue(code, 40);
        Name = Category.Required(name, 80);
        ConfigureDispatch(isPrimary, requiresPrimaryDispatch, priority);
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsPrimary { get; private set; }
    public bool RequiresPrimaryDispatch { get; private set; }
    public int Priority { get; private set; }
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static RestaurantKitchenStation Create(
        Guid restaurantId,
        string code,
        string name,
        bool isPrimary,
        bool requiresPrimaryDispatch,
        int priority,
        DateTime now
    )
    {
        return new(restaurantId, code, name, isPrimary, requiresPrimaryDispatch, priority, now);
    }

    public void Update(
        string name,
        bool isActive,
        bool isPrimary,
        bool requiresPrimaryDispatch,
        int priority,
        DateTime now
    )
    {
        Name = Category.Required(name, 80);
        ConfigureDispatch(isPrimary, requiresPrimaryDispatch, priority);
        IsActive = isActive;
        Version++;
        UpdatedAtUtc = now;
    }

    private void ConfigureDispatch(bool isPrimary, bool requiresPrimaryDispatch, int priority)
    {
        if (isPrimary)
        {
            IsPrimary = true;
            RequiresPrimaryDispatch = false;
            Priority = 0;
            return;
        }

        if (priority is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priority),
                "Non-primary KDS priority must be between 1 and 99."
            );
        }
        IsPrimary = false;
        RequiresPrimaryDispatch = requiresPrimaryDispatch;
        Priority = priority;
    }
}
