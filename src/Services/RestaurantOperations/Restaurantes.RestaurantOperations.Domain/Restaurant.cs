namespace Restaurantes.RestaurantOperations.Domain;

public sealed class Restaurant
{
    private Restaurant() { }

    private Restaurant(
        Guid id,
        string code,
        string name,
        string address,
        Guid companyId,
        DateTime occurredAtUtc
    )
    {
        Id = id;
        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name));
        Address = NormalizeRequired(address, nameof(address));
        CompanyId = companyId;
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public Guid? CompanyId { get; private set; }
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Restaurant Create(
        string code,
        string name,
        string address,
        Guid companyId,
        DateTime occurredAtUtc
    )
    {
        return new(Guid.NewGuid(), code, name, address, companyId, occurredAtUtc);
    }

    public void Update(string name, string address, DateTime occurredAtUtc)
    {
        Name = NormalizeRequired(name, nameof(name));
        Address = NormalizeRequired(address, nameof(address));
        Updated(occurredAtUtc);
    }

    public void SetCompany(Guid companyId, DateTime occurredAtUtc)
    {
        CompanyId = companyId;
        Updated(occurredAtUtc);
    }

    public void SetActive(bool isActive, DateTime occurredAtUtc)
    {
        IsActive = isActive;
        Updated(occurredAtUtc);
    }

    private void Updated(DateTime occurredAtUtc)
    {
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
