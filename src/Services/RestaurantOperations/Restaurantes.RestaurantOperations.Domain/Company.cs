namespace Restaurantes.RestaurantOperations.Domain;

public sealed class Company
{
    private Company() { }

    private Company(
        Guid id,
        string legalName,
        string taxId,
        string fiscalAddress,
        string? phone,
        string? email,
        DateTime occurredAtUtc
    )
    {
        Id = id;
        UpdateDetails(legalName, taxId, fiscalAddress, phone, email);
        Version = 1;
        UpdatedAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public string FiscalAddress { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Company Create(
        string legalName,
        string taxId,
        string fiscalAddress,
        string? phone,
        string? email,
        DateTime occurredAtUtc
    ) => new(Guid.NewGuid(), legalName, taxId, fiscalAddress, phone, email, occurredAtUtc);

    public void Update(
        string legalName,
        string taxId,
        string fiscalAddress,
        string? phone,
        string? email,
        DateTime occurredAtUtc
    )
    {
        UpdateDetails(legalName, taxId, fiscalAddress, phone, email);
        Version++;
        UpdatedAtUtc = occurredAtUtc;
    }

    private void UpdateDetails(
        string legalName,
        string taxId,
        string fiscalAddress,
        string? phone,
        string? email
    )
    {
        LegalName = Required(legalName, nameof(legalName));
        TaxId = Required(taxId, nameof(taxId)).Replace(" ", string.Empty).ToUpperInvariant();
        FiscalAddress = Required(fiscalAddress, nameof(fiscalAddress));
        Phone = Optional(phone);
        Email = Optional(email);
    }

    private static string Required(string value, string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("A non-empty value is required.", name);
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
