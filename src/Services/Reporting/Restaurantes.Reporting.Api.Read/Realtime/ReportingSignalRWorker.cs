using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Contracts.Events;

namespace Restaurantes.Reporting.Api.Read.Realtime;

public sealed class ReportingSignalRWorker(
    IHubContext<ReportingHub> hub,
    IOptions<RabbitMqOptions> options,
    ILogger<ReportingSignalRWorker> log
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
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("reporting-signalr", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await ReportingTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    ReportingTopology.SignalRQueueName,
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
                log.LogError(e, "Reporting SignalR disconnected; retrying.");
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
            if (ea.BasicProperties.Type != typeof(DashboardProjectionUpdated).FullName)
            {
                throw new NotSupportedException(ea.BasicProperties.Type);
            }

            DashboardProjectionUpdated updated =
                JsonSerializer.Deserialize<DashboardProjectionUpdated>(ea.Body.Span)
                ?? throw new JsonException("Empty dashboard projection event");
            await hub
                .Clients.Group(ReportingHub.GroupName(updated.RestaurantId))
                .SendAsync(
                    "DashboardUpdated",
                    new
                    {
                        updated.OrderId,
                        updated.RestaurantId,
                        updated.ProjectionUpdatedAtUtc,
                    }
                );
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e)
            when (e is JsonException or NotSupportedException or KeyNotFoundException)
        {
            log.LogError(e, "Invalid reporting realtime event");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Reporting realtime event will retry");
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
