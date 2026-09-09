using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Dining.Application;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Dining.Consumer;

public sealed class DiningIntegrationWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<DiningIntegrationWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory factory = RabbitMqConnectionFactory.Create(options.Value);
                connection = await factory.CreateConnectionAsync("dining-table-sessions", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await DiningTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(DiningTopology.QueueName, false, consumer, ct);
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Dining integration disconnected; retrying.");
                await DisposeBroker();
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task Handle(object sender, BasicDeliverEventArgs ea)
    {
        if (channel is null)
        {
            return;
        }

        try
        {
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("MessageId is invalid.");
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            DiningIntegrationService service =
                scope.ServiceProvider.GetRequiredService<DiningIntegrationService>();
            await service.ProcessAsync(messageId, ea.BasicProperties.Type ?? string.Empty, ea.Body);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid Dining integration event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Dining integration event will retry.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
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
