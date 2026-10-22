using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Reporting.Infrastructure;
using Restaurantes.Reporting.Infrastructure.Health;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Queries;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingInfrastructureRegistrationTests
{
    [Fact]
    public void Registration_requires_business_timezone()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddReportingInfrastructure(
                Configuration(new Dictionary<string, string?>())
            )
        );

        Assert.Equal("Business:TimeZoneId is required.", error.Message);
    }

    [Fact]
    public void Registration_adds_read_store_health_services_and_postgres_context()
    {
        IConfiguration configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["Business:TimeZoneId"] = "UTC",
                ["ConnectionStrings:ReportingRead"] = "Host=localhost;Database=reporting",
            }
        );
        ServiceCollection services = new();

        services.AddSingleton(configuration);
        services.AddReportingInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            provider.GetRequiredService<ReportingReadDbContext>().Database.ProviderName
        );
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.IsType<ReportingReadStore>(provider.GetRequiredService<IReportingReadStore>());
        Assert.IsType<ConfigurationMonitoredEndpointCatalog>(
            provider.GetRequiredService<IMonitoredEndpointCatalog>()
        );
        Assert.IsType<HttpEndpointHealthProbe>(provider.GetRequiredService<IEndpointHealthProbe>());
        Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
