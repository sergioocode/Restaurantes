using Restaurantes.Reporting.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingProjectionWorkerShutdownTests
{
    [Fact]
    public async Task StopAsync_handles_a_worker_that_never_started()
    {
        ReportingProjectionWorker worker = new(null!, null!, null!);

        Task stopTask = worker.StopAsync(CancellationToken.None);
        await stopTask;

        Assert.True(stopTask.IsCompletedSuccessfully);
    }
}
