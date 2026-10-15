using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Domain;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;

namespace Restaurantes.RestaurantOperations.Infrastructure.Stores;

public sealed class CompanyWriteStore(RestaurantWriteDbContext dbContext) : ICompanyWriteStore
{
    public Task<bool> TaxIdExistsAsync(
        string normalizedTaxId,
        Guid? excludingCompanyId,
        CancellationToken cancellationToken
    ) =>
        excludingCompanyId is Guid companyId
            ? dbContext.Companies.AnyAsync(
                item => item.TaxId == normalizedTaxId && item.Id != companyId,
                cancellationToken
            )
            : dbContext.Companies.AnyAsync(
                item => item.TaxId == normalizedTaxId,
                cancellationToken
            );

    public Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Companies.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task SaveWithEventAsync(
        Company company,
        CompanyChanged integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (dbContext.Entry(company).State == EntityState.Detached)
        {
            dbContext.Companies.Add(company);
        }

        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(CompanyChanged).FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent),
                OccurredAtUtc = integrationEvent.OccurredAtUtc,
            }
        );

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
