using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class CreateCategoryRequest : IValidatableObject
{
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; init; } = "";

    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [StringLength(40), RegularExpression("^[A-Za-z0-9_-]*$")]
    public string DefaultStationCode { get; init; } = "";

    [StringLength(80)]
    public string DefaultStationName { get; init; } = "";
    public bool RequiresKds { get; init; }
    public bool ShowInPos { get; init; } = true;
    public bool ShowInCommander { get; init; } = true;
    public bool ShowInCustomerQr { get; init; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequiresKds && string.IsNullOrWhiteSpace(DefaultStationCode))
        {
            yield return new ValidationResult(
                "Selecciona una estación KDS para las categorías que requieren preparación.",
                [nameof(DefaultStationCode)]
            );
        }
        if (RequiresKds && string.IsNullOrWhiteSpace(DefaultStationName))
        {
            yield return new ValidationResult(
                "La estación KDS seleccionada debe tener un nombre.",
                [nameof(DefaultStationName)]
            );
        }
    }
}
