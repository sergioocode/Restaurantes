using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class CreateCategoryRequest
{
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; init; } = "";

    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string DefaultStationCode { get; init; } = "GENERAL";

    [Required, StringLength(80)]
    public string DefaultStationName { get; init; } = "General";
}
