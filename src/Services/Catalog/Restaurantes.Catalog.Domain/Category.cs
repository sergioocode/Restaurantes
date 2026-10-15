namespace Restaurantes.Catalog.Domain;

public sealed class Category
{
    private Category() { }

    private Category(
        string code,
        string name,
        string stationCode,
        string stationName,
        DateTime now,
        bool showInPos,
        bool showInCommander,
        bool showInCustomerQr,
        bool requiresKds
    )
    {
        Id = Guid.NewGuid();
        Code = CodeValue(code, 40);
        Name = Required(name, 120);
        DefaultStationCode = NormalizeStationCode(stationCode, requiresKds);
        DefaultStationName = NormalizeStationName(stationName, requiresKds);
        RequiresKds = requiresKds;
        IsActive = true;
        ShowInPos = showInPos;
        ShowInCommander = showInCommander;
        ShowInCustomerQr = showInCustomerQr;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string DefaultStationCode { get; private set; } = "";
    public string DefaultStationName { get; private set; } = "";
    public bool RequiresKds { get; private set; } = true;
    public bool IsActive { get; private set; }
    public bool ShowInPos { get; private set; } = true;
    public bool ShowInCommander { get; private set; } = true;
    public bool ShowInCustomerQr { get; private set; } = true;
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Category Create(
        string code,
        string name,
        string stationCode,
        string stationName,
        DateTime now,
        bool showInPos = true,
        bool showInCommander = true,
        bool showInCustomerQr = true,
        bool requiresKds = true
    )
    {
        return new(
            code,
            name,
            stationCode,
            stationName,
            now,
            showInPos,
            showInCommander,
            showInCustomerQr,
            requiresKds
        );
    }

    public void Update(
        string name,
        string stationCode,
        string stationName,
        bool isActive,
        DateTime now,
        bool showInPos,
        bool showInCommander,
        bool showInCustomerQr,
        bool requiresKds
    )
    {
        Name = Required(name, 120);
        DefaultStationCode = NormalizeStationCode(stationCode, requiresKds);
        DefaultStationName = NormalizeStationName(stationName, requiresKds);
        RequiresKds = requiresKds;
        IsActive = isActive;
        ShowInPos = showInPos;
        ShowInCommander = showInCommander;
        ShowInCustomerQr = showInCustomerQr;
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

    private static string NormalizeStationCode(string value, bool requiresKds) =>
        requiresKds || !string.IsNullOrWhiteSpace(value) ? CodeValue(value, 40) : "";

    private static string NormalizeStationName(string value, bool requiresKds) =>
        requiresKds || !string.IsNullOrWhiteSpace(value) ? Required(value, 80) : "";

    internal static string CodeValue(string value, int max)
    {
        string result = Required(value, max).ToUpperInvariant();
        return !result.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? throw new ArgumentException("Code may only contain letters, numbers, '-' and '_'.")
            : result;
    }
}
