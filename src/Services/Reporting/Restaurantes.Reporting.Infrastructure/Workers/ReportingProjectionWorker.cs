using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

namespace Restaurantes.Reporting.Infrastructure.Workers;

public sealed class ReportingProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<ReportingProjectionWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CancellationToken ct = stoppingToken;
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<ReportingReadDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("reporting-dashboard", ct);
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await ReportingTopology.DeclareAsync(channel, ct);
                await channel.BasicQosAsync(0, 20, false, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += HandleMessageAsync;
                await channel.BasicConsumeAsync(
                    ReportingTopology.ReadModelQueueName,
                    false,
                    consumer,
                    ct
                );
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Reporting consumer disconnected; retrying.");
                await DisposeBroker();
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (channel is null)
        {
            return;
        }

        try
        {
            if (!Guid.TryParse(eventArgs.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("Invalid MessageId");
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            ReportingProjectionHandler handler =
                scope.ServiceProvider.GetRequiredService<ReportingProjectionHandler>();
            await handler.ProjectAsync(
                messageId,
                eventArgs.BasicProperties.Type,
                eventArgs.Body,
                eventArgs.CancellationToken
            );
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            log.LogError(exception, "Invalid reporting event");
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false);
        }
        catch (Exception exception)
        {
            RabbitMqRetryResult retry = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
                channel,
                eventArgs,
                ReportingTopology.ReadModelQueueName,
                exception,
                eventArgs.CancellationToken
            );
            log.LogError(
                exception,
                "Reporting event failed. Retry scheduled: {RetryScheduled}; attempt {RetryAttempt}/{MaximumAttempts}; delay {RetryDelay}.",
                retry.Scheduled,
                retry.Attempt,
                RabbitMqRetry.MaximumAttempts,
                retry.Delay
            );
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeBroker();
    }

    private async Task DisposeBroker()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
            channel = null;
        }
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }
    }
}
