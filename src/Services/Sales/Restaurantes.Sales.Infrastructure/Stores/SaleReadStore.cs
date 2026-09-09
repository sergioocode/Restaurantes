using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Contracts.Responses;
using Restaurantes.Sales.Infrastructure.Persistence.Read;

namespace Restaurantes.Sales.Infrastructure.Stores;

public sealed class SaleReadStore(SaleReadDbContext dbContext) : ISaleReadStore
{
    public async Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        SaleReadModel? sale = await dbContext
            .Sales.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return sale is null ? null : Map(sale);
    }

    public async Task<IReadOnlyList<SaleResponse>> ListAsync(
        Guid? restaurantId,
        DateOnly? date,
        CancellationToken cancellationToken
    )
    {
        IQueryable<SaleReadModel> query = dbContext.Sales.AsNoTracking();
        if (restaurantId.HasValue)
        {
            query = query.Where(item => item.RestaurantId == restaurantId.Value);
        }
        if (date.HasValue)
        {
            DateTime start = DateTime
                .SpecifyKind(date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local)
                .ToUniversalTime();
            DateTime end = start.AddDays(1);
            query = query.Where(item => item.CompletedAtUtc >= start && item.CompletedAtUtc < end);
        }

        return (
            await query
                .OrderByDescending(item => item.CompletedAtUtc)
                .Take(100)
                .ToListAsync(cancellationToken)
        )
            .Select(Map)
            .ToList();
    }

    private static SaleResponse Map(SaleReadModel sale)
    {
        return new(
            sale.Id,
            sale.RestaurantId,
            sale.Source,
            sale.PaymentMethod,
            sale.Total,
            sale.Status,
            sale.OrderCount,
            sale.CompletedAtUtc,
            JsonSerializer.Deserialize<List<Guid>>(sale.OrderIdsJson) ?? [],
            JsonSerializer.Deserialize<List<SaleLineSnapshot>>(sale.LinesJson) ?? []
        );
    }
}
