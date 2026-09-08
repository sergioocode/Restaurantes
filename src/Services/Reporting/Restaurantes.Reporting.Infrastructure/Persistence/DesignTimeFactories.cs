using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

namespace Restaurantes.Reporting.Infrastructure.Persistence;

public sealed class ReportingReadDbContextFactory
    : IDesignTimeDbContextFactory<ReportingReadDbContext>
{
    public ReportingReadDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=reporting_read;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
    }
}
