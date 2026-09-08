namespace Restaurantes.Reporting.Contracts;

public sealed record DashboardProjectionUpdated(
    Guid OrderId,
    Guid RestaurantId,
    DateTime ProjectionUpdatedAtUtc
);
