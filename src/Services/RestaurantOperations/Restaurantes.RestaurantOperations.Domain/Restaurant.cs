namespace Restaurantes.RestaurantOperations.Domain;

public sealed class Restaurant
{
    private Restaurant() { }

    private Restaurant(Guid id, string code, string name, string address, DateTime occurredAtUtc)
    {
        Id = id;
        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name));
        Address = NormalizeRequired(address, nameof(address));
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Restaurant Create(
        string code,
        string name,
        string address,
        DateTime occurredAtUtc
    )
    {
        return new(Guid.NewGuid(), code, name, address, occurredAtUtc);
    }

    public void Update(string name, string address, bool isActive, DateTime occurredAtUtc)
    {
        Name = NormalizeRequired(name, nameof(name));
        Address = NormalizeRequired(address, nameof(address));
        IsActive = isActive;
        Version++;
        UpdatedAtUtc = occurredAtUtc;
    }

    private static string NormalizeCode(string value)
    {
        string normalized = NormalizeRequired(value, nameof(value)).ToUpperInvariant();
        return normalized.Length <= 40
            ? normalized
            : throw new ArgumentException(
                "Restaurant code cannot exceed 40 characters.",
                nameof(value)
            );
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        string normalized = value.Trim();
        return !string.IsNullOrWhiteSpace(normalized)
            ? normalized
            : throw new ArgumentException("A non-empty value is required.", parameterName);
    }
}
