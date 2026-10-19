using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class UpdateKitchenStationRequest
{
    [Required, StringLength(80)]
    public string Name { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public bool IsPrimary { get; init; }
    public bool RequiresPrimaryDispatch { get; init; }
    public bool? PrintTicket { get; init; }

    [Range(0, 99)]
    public int Priority { get; init; } = 1;
}
