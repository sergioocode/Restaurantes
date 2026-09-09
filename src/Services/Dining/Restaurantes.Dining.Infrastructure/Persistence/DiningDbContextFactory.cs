using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Restaurantes.Dining.Infrastructure.Persistence;

public sealed class DiningDbContextFactory : IDesignTimeDbContextFactory<DiningDbContext>
{
    public DiningDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DiningWrite")
            ?? "Host=localhost;Port=5432;Database=dining_write;Username=restaurants;Password=restaurants_dev";
        return new DiningDbContext(
            new DbContextOptionsBuilder<DiningDbContext>().UseNpgsql(connectionString).Options
        );
    }
}
