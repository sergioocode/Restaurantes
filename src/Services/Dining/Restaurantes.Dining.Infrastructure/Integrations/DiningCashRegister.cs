using Restaurantes.Dining.Application;
using Restaurantes.Security;

namespace Restaurantes.Dining.Infrastructure.Integrations;

public sealed class DiningCashRegister(CashRegisterAvailabilityClient client) : IDiningCashRegister
{
    public async Task EnsureOpenAsync(Guid restaurantId, CancellationToken ct)
    {
        try
        {
            await client.EnsureOpenAsync(restaurantId, ct);
        }
        catch (CashRegisterClosedException e)
        {
            throw new DiningCashRegisterClosedException(e.Message);
        }
    }
}
