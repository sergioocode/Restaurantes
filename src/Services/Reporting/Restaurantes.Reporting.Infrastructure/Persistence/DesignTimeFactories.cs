using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Reporting.Infrastructure.Persistence;

public sealed class ReportingReadDbContextFactory
    : IDesignTimeDbContextFactory<ReportingReadDbContext>
{
    public ReportingReadDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("ReportingRead")
                )
                .Options
        );
    }
}
