using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Dining.Infrastructure.Persistence;

public sealed class DiningDbContextFactory : IDesignTimeDbContextFactory<DiningDbContext>
{
    public DiningDbContext CreateDbContext(string[] args)
    {
        string connectionString = VaultConfigurationExtensions.GetDesignTimeConnectionString(
            "DiningWrite"
        );
        return new DiningDbContext(
            new DbContextOptionsBuilder<DiningDbContext>().UseNpgsql(connectionString).Options
        );
    }
}
