using Microsoft.Extensions.Hosting;

namespace Restaurantes.ServiceDefaults;

public abstract class AsyncDisposingBackgroundService : BackgroundService
{
    public sealed override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeResourcesAsync();
    }

    protected abstract Task DisposeResourcesAsync();
}
