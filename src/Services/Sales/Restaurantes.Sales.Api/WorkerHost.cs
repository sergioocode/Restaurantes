using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Infrastructure;
using Restaurantes.Sales.Infrastructure.Workers;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSalesWriteInfrastructure(configuration);
        services.AddSalesReadInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SaleCommandService>();
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options =>
                    options.Port > 0
                    && !string.IsNullOrWhiteSpace(options.HostName)
                    && !string.IsNullOrWhiteSpace(options.UserName)
                    && !string.IsNullOrWhiteSpace(options.Password),
                "RabbitMq invalid"
            )
            .ValidateOnStart();
        services.AddHostedService<CheckoutProcessWorker>();
        services.AddHostedService<SalesReadProjectionWorker>();
        services.AddHostedService<SalesOutboxPublisherWorker>();
    }

}
