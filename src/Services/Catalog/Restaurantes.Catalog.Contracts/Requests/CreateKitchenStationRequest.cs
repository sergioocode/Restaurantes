using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class CreateKitchenStationRequest
{
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; init; } = "";

    [Required, StringLength(80)]
    public string Name { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool RequiresPrimaryDispatch { get; init; }

    [Range(0, 99)]
    public int Priority { get; init; } = 1;
}
