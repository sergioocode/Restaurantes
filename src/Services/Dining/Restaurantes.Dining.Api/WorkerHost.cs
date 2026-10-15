using Restaurantes.Dining.Application;
using Restaurantes.Dining.Infrastructure;
using Restaurantes.Dining.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Dining.Api;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDiningPersistence(configuration);
        services.AddScoped<DiningIntegrationService>();
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
        services.AddHostedService<DiningIntegrationWorker>();
    }
}
