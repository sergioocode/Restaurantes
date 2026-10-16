using Restaurantes.Catalog.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogProjectionWorkerShutdownTests
{
    [Fact]
    public async Task StopAsync_handles_a_worker_that_never_started()
    {
        CatalogProjectionWorker worker = new(null!, null!, null!, null!);

        Task stopTask = worker.StopAsync(CancellationToken.None);
        await stopTask;

        Assert.True(stopTask.IsCompletedSuccessfully);
    }
}
