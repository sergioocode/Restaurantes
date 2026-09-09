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
