using System.ComponentModel.DataAnnotations;

namespace Restaurantes.RestaurantOperations.Contracts.Requests;

public sealed class SetRestaurantCompanyRequest
{
    [Required]
    public Guid? CompanyId { get; init; }
}
