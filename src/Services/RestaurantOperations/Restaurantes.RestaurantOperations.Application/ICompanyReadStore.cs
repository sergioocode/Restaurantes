using Restaurantes.RestaurantOperations.Contracts.Responses;

namespace Restaurantes.RestaurantOperations.Application;

public interface ICompanyReadStore
{
    Task<CompanyResponse?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken);
}
