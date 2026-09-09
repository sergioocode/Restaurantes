namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderDetailResponse(
    Guid Id,
    Guid RestaurantId,
    string Status,
    decimal Total,
    List<OrderLineDetailResponse> Lines,
    List<OrderStationDetailResponse> Stations
);
