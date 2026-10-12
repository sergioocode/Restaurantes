using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Application;

public interface ICompanyWriteStore
{
    Task<bool> TaxIdExistsAsync(
        string normalizedTaxId,
        Guid? excludingCompanyId,
        CancellationToken cancellationToken
    );
    Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveWithEventAsync(
        Company company,
        CompanyChanged integrationEvent,
        CancellationToken cancellationToken
    );
}
