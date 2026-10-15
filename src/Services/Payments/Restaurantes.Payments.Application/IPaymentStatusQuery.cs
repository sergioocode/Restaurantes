using Restaurantes.Payments.Contracts.Responses;

namespace Restaurantes.Payments.Application;

public interface IPaymentStatusQuery
{
    Task<IReadOnlyList<OrderPaymentStatusResponse>> ListAsync(
        Guid restaurantId,
        Guid[] orderIds,
        CancellationToken ct
    );
}
