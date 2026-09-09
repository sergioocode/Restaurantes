namespace Restaurantes.Reporting.Contracts.Events;

public sealed record DashboardProjectionUpdated(
    Guid OrderId,
    Guid RestaurantId,
    DateTime ProjectionUpdatedAtUtc
);
