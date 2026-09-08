using System.ComponentModel.DataAnnotations;
using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; init; } = "localhost";

    [Range(1, 65_535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string UserName { get; init; } = "restaurants";

    [Required]
    public string Password { get; init; } = "restaurants_dev";

    [Required]
    public string VirtualHost { get; init; } = "/";
}

public static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(RabbitMqOptions options)
    {
        return new()
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        };
    }
}
