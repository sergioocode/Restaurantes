using Restaurantes.Catalog.Infrastructure;
using Restaurantes.Catalog.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCatalogReadInfrastructure(configuration);
        services.AddCatalogWriteInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.HostName)
                    && options.Port > 0
                    && !string.IsNullOrWhiteSpace(options.UserName)
                    && !string.IsNullOrWhiteSpace(options.Password),
                "RabbitMq configuration is invalid."
            )
            .ValidateOnStart();
        services.AddHostedService<CatalogProjectionWorker>();
        services.AddHostedService<CatalogOutboxPublisherWorker>();
    }
}
