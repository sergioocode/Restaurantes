using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Restaurantes.Identity.Api.Write;

public sealed class IdentitySeed(
    IdentityWriteDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    TimeProvider time
)
{
    private static readonly string[] RoleNames =
    [
        "Admin",
        "Gerente",
        "Oficina",
        "Manager",
        "PosComandero",
        "Kds",
    ];

    private static readonly HashSet<string> GlobalRoles = ["Admin", "Gerente", "Oficina"];

    private static readonly RestaurantSeed[] Restaurants =
    [
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "mad-centro", "Madrid Centro"),
        new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "mad-norte", "Madrid Norte"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333333"), "bcn-centro", "Barcelona Centro"),
        new(Guid.Parse("44444444-4444-4444-8444-444444444444"), "val-centro", "Valencia Centro"),
        new(Guid.Parse("55555555-5555-4555-8555-555555555555"), "sev-centro", "Sevilla Centro"),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (string role in RoleNames)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole<Guid>(role)));
            }
        }

        Guid[] allRestaurantIds = Restaurants.Select(item => item.Id).ToArray();
        await EnsureUserAsync(
            "admin",
            "Admin-2026!",
            "Administrador de la plataforma",
            "Admin",
            allRestaurantIds,
            ct
        );
        await EnsureUserAsync(
            "gerente.contabilidad",
            "Gerente-Contabilidad-2026!",
            "Gerente · Contabilidad",
            "Gerente",
            allRestaurantIds,
            ct
        );
        await EnsureUserAsync(
            "gerente.rrhh",
            "Gerente-RRHH-2026!",
            "Gerente · Recursos Humanos",
            "Gerente",
            allRestaurantIds,
            ct
        );
        await EnsureUserAsync(
            "marketing",
            "Marketing-2026!",
            "Oficina · Marketing",
            "Oficina",
            allRestaurantIds,
            ct
        );

        for (int index = 0; index < Restaurants.Length; index++)
        {
            RestaurantSeed restaurant = Restaurants[index];
            int number = index + 1;
            await EnsureUserAsync(
                $"manager.{restaurant.Slug}",
                $"Manager-{number:00}-2026!",
                $"Manager · {restaurant.Name}",
                "Manager",
                [restaurant.Id],
                ct
            );
            for (int waiter = 1; waiter <= 2; waiter++)
            {
                await EnsureUserAsync(
                    $"camarero{waiter}.{restaurant.Slug}",
                    $"Camarero-{number:00}-{waiter}-2026!",
                    $"Camarero {waiter} · {restaurant.Name}",
                    "PosComandero",
                    [restaurant.Id],
                    ct
                );
            }
            await EnsureUserAsync(
                $"kds.{restaurant.Slug}",
                $"Kds-{number:00}-2026!",
                $"KDS compartido · {restaurant.Name}",
                "Kds",
                [restaurant.Id],
                ct
            );
        }
    }

    private async Task EnsureUserAsync(
        string username,
        string password,
        string displayName,
        string role,
        IReadOnlyCollection<Guid> restaurantIds,
        CancellationToken ct
    )
    {
        ApplicationUser? user = await users.FindByNameAsync(username);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = username,
                DisplayName = displayName,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                IsActive = true,
                LockoutEnabled = true,
            };
            EnsureSucceeded(await users.CreateAsync(user, password));
        }

        if (GlobalRoles.Contains(role) && !await users.IsInRoleAsync(user, role))
        {
            EnsureSucceeded(await users.AddToRoleAsync(user, role));
        }

        HashSet<Guid> assignedRestaurantIds = await db
            .RestaurantAccesses.AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .Select(item => item.RestaurantId)
            .ToHashSetAsync(ct);
        DateTime now = time.GetUtcNow().UtcDateTime;
        foreach (Guid restaurantId in restaurantIds)
        {
            if (assignedRestaurantIds.Contains(restaurantId))
            {
                continue;
            }

            db.RestaurantAccesses.Add(
                new UserRestaurantAssignment
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RestaurantId = restaurantId,
                    Role = role,
                    ValidFromUtc = now,
                }
            );
        }
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(item => item.Description))
            );
        }
    }

    private sealed record RestaurantSeed(Guid Id, string Slug, string Name);
}
