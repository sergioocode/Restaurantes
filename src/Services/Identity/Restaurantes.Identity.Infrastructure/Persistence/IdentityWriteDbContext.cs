using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Identity.Domain;

namespace Restaurantes.Identity.Infrastructure.Persistence;

public sealed class IdentityWriteDbContext(DbContextOptions<IdentityWriteDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<UserRestaurantAssignment> RestaurantAccesses => Set<UserRestaurantAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        EntityTypeBuilder<ApplicationUser> user = modelBuilder.Entity<ApplicationUser>();
        user.ToTable("staff_users");
        user.Property(x => x.UserName).HasColumnName("Username").HasMaxLength(80).IsRequired();
        user.Property(x => x.NormalizedUserName)
            .HasColumnName("NormalizedUsername")
            .HasMaxLength(80)
            .IsRequired();
        user.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        user.Property(x => x.PasswordHash).HasMaxLength(600).IsRequired();
        user.HasIndex(x => x.NormalizedUserName).IsUnique();
        user.HasMany(x => x.RestaurantAccesses)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("identity_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("identity_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("identity_user_claims");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("identity_role_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("identity_user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("identity_user_tokens");

        EntityTypeBuilder<UserRestaurantAssignment> access =
            modelBuilder.Entity<UserRestaurantAssignment>();
        access.ToTable("user_restaurant_accesses");
        access.HasKey(x => x.Id);
        access.Property(x => x.Role).HasMaxLength(40).IsRequired();
        access.HasIndex(x => new { x.UserId, x.RestaurantId }).IsUnique();
    }
}
