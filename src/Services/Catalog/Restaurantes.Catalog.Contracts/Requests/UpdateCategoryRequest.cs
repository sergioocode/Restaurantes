using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class UpdateCategoryRequest : IValidatableObject
{
    public Guid? RestaurantId { get; init; }

    [Required, StringLength(120)]
    public string Name { get; init; } = "";

    [StringLength(40), RegularExpression("^[A-Za-z0-9_-]*$")]
    public string? DefaultStationCode { get; init; }

    [StringLength(80)]
    public string? DefaultStationName { get; init; }
    public bool? RequiresKds { get; init; }
    public bool IsActive { get; init; } = true;
    public bool? ShowInPos { get; init; }
    public bool? ShowInCommander { get; init; }
    public bool? ShowInCustomerQr { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequiresKds is true && string.IsNullOrWhiteSpace(DefaultStationCode))
        {
            yield return new ValidationResult(
                "Selecciona una estación KDS para las categorías que requieren preparación.",
                [nameof(DefaultStationCode)]
            );
        }
        if (RequiresKds is true && string.IsNullOrWhiteSpace(DefaultStationName))
        {
            yield return new ValidationResult(
                "La estación KDS seleccionada debe tener un nombre.",
                [nameof(DefaultStationName)]
            );
        }
    }
}
