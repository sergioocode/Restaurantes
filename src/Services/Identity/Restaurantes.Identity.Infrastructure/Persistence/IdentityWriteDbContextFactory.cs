using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Identity.Infrastructure.Persistence;

public sealed class IdentityWriteDbContextFactory
    : IDesignTimeDbContextFactory<IdentityWriteDbContext>
{
    public IdentityWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<IdentityWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("IdentityWrite")
                )
                .Options
        );
    }
}
