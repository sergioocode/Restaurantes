using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure;
using Restaurantes.CashRegister.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCashRegisterInfrastructure(configuration);
        services.AddScoped<CashPaymentProjectionService>();
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
        services.AddHostedService<CashPaymentProjectionWorker>();
    }
}
