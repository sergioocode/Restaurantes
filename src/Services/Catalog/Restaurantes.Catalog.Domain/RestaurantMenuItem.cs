namespace Restaurantes.Catalog.Domain;

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
