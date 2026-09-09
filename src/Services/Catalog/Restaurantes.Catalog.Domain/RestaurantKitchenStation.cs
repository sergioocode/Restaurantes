namespace Restaurantes.Catalog.Domain;

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
