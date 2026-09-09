using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class ConfigureMenuItemRequest
{
    [Range(typeof(decimal), "0", "9999999")]
    public decimal Price { get; init; }
    public bool IsAvailable { get; init; } = true;

    [StringLength(40), RegularExpression("^[A-Za-z0-9_-]*$")]
    public string? PreparationStationCode { get; init; }

    [StringLength(80)]
    public string? PreparationStationName { get; init; }
}
