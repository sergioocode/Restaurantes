using System.ComponentModel.DataAnnotations;

namespace Restaurantes.RestaurantOperations.Contracts.Requests;

public sealed class CreateCompanyRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string LegalName { get; init; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 3)]
    public string TaxId { get; init; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 3)]
    public string FiscalAddress { get; init; } = string.Empty;

    [StringLength(40)]
    public string? Phone { get; init; }

    [EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}
