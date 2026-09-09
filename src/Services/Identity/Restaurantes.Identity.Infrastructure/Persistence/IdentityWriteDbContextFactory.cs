using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Restaurantes.Identity.Infrastructure.Persistence;

public sealed class IdentityWriteDbContextFactory
    : IDesignTimeDbContextFactory<IdentityWriteDbContext>
{
    public IdentityWriteDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<IdentityWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=identity_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
}
