using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Reporting.Infrastructure.Health;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Queries;

namespace Restaurantes.Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<ReportingReadDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ReportingRead"))
        );
        services.AddHttpClient(nameof(HttpEndpointHealthProbe));
        services.AddScoped<IReportingReadStore, ReportingReadStore>();
        services.AddSingleton<IMonitoredEndpointCatalog, ConfigurationMonitoredEndpointCatalog>();
        services.AddSingleton<IEndpointHealthProbe, HttpEndpointHealthProbe>();
        return services;
    }

    public static async Task MigrateReportingDatabaseAsync(
        this IServiceProvider services,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ReportingReadDbContext db =
            scope.ServiceProvider.GetRequiredService<ReportingReadDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
