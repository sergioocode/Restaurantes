using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Infrastructure.Workers;
using Restaurantes.Orders.Infrastructure.Workers.Handlers;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOrdersReadInfrastructure(configuration);
        services.AddOrdersWriteInfrastructure(configuration);
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
        services.AddScoped<KdsProjectionHandler>();
        services.AddHostedService<KdsProjectionWorker>();
        services.AddHostedService<CatalogProjectionWorker>();
        services.AddHostedService<PaymentProjectionWorker>();
        services.AddHostedService<OrderOutboxPublisherWorker>();
    }
}
