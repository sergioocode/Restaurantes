using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Restaurantes.Identity.Infrastructure.Persistence;

public sealed class IdentityWriteDbContextFactory
    : IDesignTimeDbContextFactory<IdentityWriteDbContext>
{
    public IdentityWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<IdentityWriteDbContext>()
                .UseNpgsql(
                    Environment.GetEnvironmentVariable("ConnectionStrings__IdentityWrite")
                        ?? "Host=localhost;Port=5432;Database=identity_write;Username=restaurants"
                )
                .Options
        );
    }
}
