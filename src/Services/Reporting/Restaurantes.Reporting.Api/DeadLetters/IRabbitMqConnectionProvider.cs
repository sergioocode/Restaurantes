using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Reporting.Api.DeadLetters;

public interface IRabbitMqConnectionProvider
{
    Task<IConnection> ConnectAsync(string clientProvidedName, CancellationToken cancellationToken);
}

public sealed class RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options)
    : IRabbitMqConnectionProvider
{
    public Task<IConnection> ConnectAsync(
        string clientProvidedName,
        CancellationToken cancellationToken
    ) =>
        RabbitMqConnectionFactory
            .Create(options.Value)
            .CreateConnectionAsync(clientProvidedName, cancellationToken);
}
