using Restaurantes.Sales.Contracts.Responses;

namespace Restaurantes.Sales.Application;

public interface ISaleReadStore
{
    Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleResponse>> ListAsync(
        Guid? restaurantId,
        DateOnly? date,
        CancellationToken cancellationToken
    );
}
