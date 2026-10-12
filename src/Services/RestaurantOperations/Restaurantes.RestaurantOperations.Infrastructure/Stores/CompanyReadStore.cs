using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;

namespace Restaurantes.RestaurantOperations.Infrastructure.Stores;

public sealed class CompanyReadStore(RestaurantReadDbContext dbContext) : ICompanyReadStore
{
    public async Task<CompanyResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext
            .Companies.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new CompanyResponse(
                item.Id,
                item.LegalName,
                item.TaxId,
                item.FiscalAddress,
                item.Phone,
                item.Email,
                item.Version,
                item.UpdatedAtUtc
            ))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CompanyResponse>> ListAsync(
        CancellationToken cancellationToken
    ) =>
        await dbContext
            .Companies.AsNoTracking()
            .OrderBy(item => item.LegalName)
            .Select(item => new CompanyResponse(
                item.Id,
                item.LegalName,
                item.TaxId,
                item.FiscalAddress,
                item.Phone,
                item.Email,
                item.Version,
                item.UpdatedAtUtc
            ))
            .ToListAsync(cancellationToken);
}
