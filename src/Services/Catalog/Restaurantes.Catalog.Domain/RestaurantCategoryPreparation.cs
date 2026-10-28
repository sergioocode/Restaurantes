namespace Restaurantes.Catalog.Domain;

public sealed class RestaurantCategoryPreparation
{
    private RestaurantCategoryPreparation() { }

    private RestaurantCategoryPreparation(
        Guid restaurantId,
        Guid categoryId,
        string stationCode,
        string stationName,
        DateTime now
    )
    {
        if (restaurantId == Guid.Empty || categoryId == Guid.Empty)
            throw new ArgumentException("RestaurantId and CategoryId are required.");
        RestaurantId = restaurantId;
        CategoryId = categoryId;
        Configure(stationCode, stationName, now, false);
    }

    public Guid RestaurantId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string StationCode { get; private set; } = "";
    public string StationName { get; private set; } = "";
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static RestaurantCategoryPreparation Create(
        Guid restaurantId,
        Guid categoryId,
        string stationCode,
        string stationName,
        DateTime now
    ) => new(restaurantId, categoryId, stationCode, stationName, now);

    public void Configure(string stationCode, string stationName, DateTime now) =>
        Configure(stationCode, stationName, now, true);

    private void Configure(string stationCode, string stationName, DateTime now, bool increment)
    {
        StationCode = string.IsNullOrWhiteSpace(stationCode)
            ? ""
            : Category.CodeValue(stationCode, 40);
        StationName = string.IsNullOrWhiteSpace(StationCode)
            ? ""
            : Category.Required(stationName, 80);
        Version = increment ? Version + 1 : 1;
        UpdatedAtUtc = now;
    }
}
