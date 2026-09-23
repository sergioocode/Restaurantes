namespace Restaurantes.Reporting.Api.Read.DeadLetters;

public sealed class DeadLetterQueueMonitor(
    RabbitMqDeadLetterService deadLetters,
    ILogger<DeadLetterQueueMonitor> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        do
        {
            try
            {
                IReadOnlyCollection<DeadLetterQueueStatus> queues =
                    await deadLetters.GetStatusAsync(stoppingToken);
                foreach (DeadLetterQueueStatus queue in queues)
                {
                    if (queue.Available && queue.MessageCount > 0)
                    {
                        logger.LogWarning(
                            "Dead-letter queue {Queue} contains {MessageCount} messages.",
                            queue.Queue,
                            queue.MessageCount
                        );
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not inspect RabbitMQ dead-letter queues.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
