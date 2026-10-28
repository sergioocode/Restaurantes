using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Restaurantes.Dining.Api.Realtime;

namespace Restaurantes.Dining.Api;

public sealed class DiningQrReservationWorker(
    IServiceScopeFactory scopes,
    ILogger<DiningQrReservationWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not expire idle QR table reservations.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExpireReservationsAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Restaurantes.Dining.Application.DiningService dining =
            scope.ServiceProvider.GetRequiredService<Restaurantes.Dining.Application.DiningService>();
        DiningNotifications notifications =
            scope.ServiceProvider.GetRequiredService<DiningNotifications>();
        List<Guid> restaurants = await dining.ExpireQrReservations(ct);
        foreach (Guid restaurantId in restaurants)
        {
            await notifications.DiningChanged(restaurantId);
        }
    }
}
