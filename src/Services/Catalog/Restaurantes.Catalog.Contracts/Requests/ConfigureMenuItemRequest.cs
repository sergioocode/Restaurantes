using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class ConfigureMenuItemRequest
{
    [Range(typeof(decimal), "0", "9999999")]
    public decimal Price { get; init; }
    public bool IsAvailable { get; init; } = true;
}
