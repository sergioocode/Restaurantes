using Restaurantes.ServiceDefaults;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class AsyncDisposingBackgroundServiceTests
{
    [Fact]
    public async Task StopAsync_disposes_resources_after_stopping_the_worker()
    {
        TestWorker worker = new();
        await worker.StartAsync(CancellationToken.None);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ResourcesDisposed);
    }

    private sealed class TestWorker : AsyncDisposingBackgroundService
    {
        public bool ResourcesDisposed { get; private set; }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.Delay(Timeout.Infinite, stoppingToken);

        protected override Task DisposeResourcesAsync()
        {
            ResourcesDisposed = true;
            return Task.CompletedTask;
        }
    }
}
