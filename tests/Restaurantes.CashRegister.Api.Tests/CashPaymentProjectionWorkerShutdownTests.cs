using Restaurantes.CashRegister.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashPaymentProjectionWorkerShutdownTests
{
    [Fact]
    public async Task StopAsync_handles_a_worker_that_never_started()
    {
        CashPaymentProjectionWorker worker = new(null!, null!, null!);

        Task stopTask = worker.StopAsync(CancellationToken.None);
        await stopTask;

        Assert.True(stopTask.IsCompletedSuccessfully);
    }
}
