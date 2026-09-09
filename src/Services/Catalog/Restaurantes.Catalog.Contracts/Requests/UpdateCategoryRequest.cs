using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class UpdateCategoryRequest
{
    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string DefaultStationCode { get; init; } = "GENERAL";

    [Required, StringLength(80)]
    public string DefaultStationName { get; init; } = "General";
    public bool IsActive { get; init; } = true;
}
