using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Infrastructure;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Workers;

namespace Restaurantes.Sales.Api;

internal static class WorkerHost
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSalesWriteInfrastructure(configuration);
        services.AddSalesReadInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SaleCommandService>();
        services.AddScoped<SaleTicketQueryService>();
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

    public static async Task MigrateDatabasesAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<SaleWriteDbContext>()
            .Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SaleReadDbContext>().Database.MigrateAsync();
    }
}
