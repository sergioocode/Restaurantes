using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Infrastructure;
using Restaurantes.Reporting.Infrastructure.Workers;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddReportingInfrastructure(configuration);
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
        services.AddScoped<ReportingProjectionHandler>();
        services.AddHostedService<ReportingProjectionWorker>();
        services.AddHostedService<ReportingOutboxPublisherWorker>();
    }
}
