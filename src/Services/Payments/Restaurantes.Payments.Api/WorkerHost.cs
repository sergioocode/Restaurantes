using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Infrastructure;
using Restaurantes.Payments.Infrastructure.Workers;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPaymentsWriteInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
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
        services.AddHostedService<PayableOrderProjectionWorker>();
        services.AddHostedService<PaymentOutboxPublisherWorker>();
    }
}
