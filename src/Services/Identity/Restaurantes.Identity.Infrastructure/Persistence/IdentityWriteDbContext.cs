using Microsoft.EntityFrameworkCore;
using Restaurantes.Identity.Domain;

namespace Restaurantes.Identity.Infrastructure.Persistence;

public sealed class IdentityWriteDbContext(DbContextOptions<IdentityWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<AuthenticationSettings> AuthenticationSettings => Set<AuthenticationSettings>();
    public DbSet<LoginTicket> LoginTickets => Set<LoginTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("authorized_accounts");
            user.HasKey(x => x.Id);
            user.Property(x => x.Email).HasMaxLength(320).IsRequired();
            user.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            user.Property(x => x.ProviderSubject).HasMaxLength(256);
            user.Property(x => x.TenantId).HasMaxLength(64);
            user.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            user.Property(x => x.Role).HasMaxLength(40).IsRequired();
            user.HasIndex(x => new { x.Provider, x.Email }).IsUnique();
            user.HasIndex(x => new
                {
                    x.Provider,
                    x.TenantId,
                    x.ProviderSubject,
                })
                .IsUnique();
        });
        modelBuilder.Entity<AuthenticationSettings>(settings =>
        {
            settings.ToTable("authentication_settings");
            settings.HasKey(x => x.Id);
            settings.Property(x => x.ActiveProvider).HasMaxLength(20).IsRequired();
        });
        modelBuilder.Entity<LoginTicket>(ticket =>
        {
            ticket.ToTable("login_tickets");
            ticket.HasKey(x => x.CodeHash);
            ticket.Property(x => x.CodeHash).HasMaxLength(64);
            ticket.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
