using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Infrastructure;
using Restaurantes.RestaurantOperations.Infrastructure.Workers;

namespace Restaurantes.RestaurantOperations.Api;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRestaurantOperationsReadInfrastructure(configuration);
        services.AddRestaurantOperationsWriteInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.HostName)
                    && options.Port is > 0 and <= 65_535
                    && !string.IsNullOrWhiteSpace(options.UserName)
                    && !string.IsNullOrWhiteSpace(options.Password)
                    && !string.IsNullOrWhiteSpace(options.VirtualHost),
                "RabbitMq configuration is invalid."
            )
            .ValidateOnStart();
        services.AddHostedService<RestaurantProjectionWorker>();
        services.AddHostedService<OutboxPublisherWorker>();
    }
}
