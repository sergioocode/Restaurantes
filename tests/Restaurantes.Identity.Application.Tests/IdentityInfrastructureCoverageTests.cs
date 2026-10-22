using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Domain;
using Restaurantes.Identity.Infrastructure;
using Restaurantes.Identity.Infrastructure.Persistence;
using Restaurantes.Identity.Infrastructure.Security;
using Restaurantes.Identity.Infrastructure.Stores;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Identity.Application.Tests;

public sealed class IdentityInfrastructureCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Identity_store_queries_accounts_and_active_administrators()
    {
        await using IdentityWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        ApplicationUser admin = User("admin@example.test", "Admin", restaurantId);
        ApplicationUser manager = User("manager@example.test", "Manager", restaurantId);
        ApplicationUser inactiveAdmin = User("inactive@example.test", "Admin", restaurantId);
        inactiveAdmin.IsActive = false;
        inactiveAdmin.DisplayName = "Inactive";
        ApplicationUser backupAdmin = User("backup@example.test", "Admin", restaurantId);
        db.Users.AddRange(manager, inactiveAdmin, admin, backupAdmin);
        db.AuthenticationSettings.Add(new AuthenticationSettings { ActiveProvider = "Google" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        IdentityStore store = new(db, new FixedTimeProvider(Now));

        Assert.Equal(
            "Google",
            (await store.GetSettingsAsync(TestContext.Current.CancellationToken)).ActiveProvider
        );
        Assert.Equal(
            admin.Id,
            (
                await store.FindByEmailAsync(
                    "Microsoft",
                    admin.Email,
                    TestContext.Current.CancellationToken
                )
            )!.Id
        );
        Assert.Equal(
            admin.Id,
            (
                await store.FindBySubjectAsync(
                    "Microsoft",
                    "tenant-1",
                    admin.Email,
                    TestContext.Current.CancellationToken
                )
            )!.Id
        );
        Assert.Equal(
            manager.Id,
            (await store.FindByIdAsync(manager.Id, TestContext.Current.CancellationToken))!.Id
        );
        Assert.Equal(
            ["Admin", "Admin", "Inactive", "Manager"],
            (await store.ListUsersAsync(TestContext.Current.CancellationToken)).Select(user =>
                user.DisplayName
            )
        );
        Assert.True(
            await store.AnyActiveAdminForProviderAsync(
                "Microsoft",
                TestContext.Current.CancellationToken
            )
        );
        Assert.True(
            await store.AnyActiveAdminExceptAsync(
                admin.Id,
                "Microsoft",
                TestContext.Current.CancellationToken
            )
        );
        Assert.False(
            await store.AnyActiveAdminExceptAsync(
                manager.Id,
                "Google",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Identity_store_persists_accounts_and_creates_hashed_short_lived_login_tickets()
    {
        await using IdentityWriteDbContext db = CreateDatabase();
        FixedTimeProvider time = new(Now);
        IdentityStore store = new(db, time);
        ApplicationUser user = User("new@example.test", "New", Guid.NewGuid());

        await store.AddUserAsync(user, TestContext.Current.CancellationToken);
        string ticket = await store.CreateLoginTicketAsync(
            user.Id,
            TestContext.Current.CancellationToken
        );
        await store.SaveChangesAsync(TestContext.Current.CancellationToken);

        LoginTicket persistedTicket = await db.LoginTickets.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(user.Id, persistedTicket.UserId);
        Assert.NotEqual(ticket, persistedTicket.CodeHash);
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), persistedTicket.ExpiresAtUtc);
        Assert.Null(persistedTicket.ConsumedAtUtc);
        Assert.Null(await store.ConsumeLoginTicketAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Identity_store_deletes_users_and_rejects_empty_or_oversized_tickets()
    {
        await using IdentityWriteDbContext db = CreateDatabase();
        ApplicationUser user = User("delete@example.test", "Delete", Guid.NewGuid());
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        IdentityStore store = new(db, new FixedTimeProvider(Now));

        Assert.Null(await store.ConsumeLoginTicketAsync("", TestContext.Current.CancellationToken));
        Assert.Null(
            await store.ConsumeLoginTicketAsync(
                new string('x', 101),
                TestContext.Current.CancellationToken
            )
        );
        await store.DeleteUserAsync(user, TestContext.Current.CancellationToken);

        Assert.Empty(db.Users);
    }

    [Fact]
    public void Access_token_contains_global_or_restaurant_scoped_claims()
    {
        AccessTokenService issuer = new(
            Options.Create(
                new RestaurantSecurityOptions
                {
                    SigningKey = new string('k', 64),
                    Issuer = "restaurantes-tests",
                    Audience = "restaurantes-clients",
                    AccessTokenMinutes = 20,
                }
            ),
            new FixedTimeProvider(Now)
        );
        ApplicationUser globalAdmin = User("admin@example.test", "Admin", null);
        IssuedAccessToken globalToken = issuer.Issue(globalAdmin);
        JwtSecurityToken global = new JwtSecurityTokenHandler().ReadJwtToken(globalToken.Value);

        Assert.Equal(Now.AddMinutes(20).UtcDateTime, globalToken.ExpiresAtUtc);
        Assert.Equal("restaurantes-tests", global.Issuer);
        Assert.Contains("restaurantes-clients", global.Audiences);
        Assert.Contains(
            global.Claims,
            claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin"
        );
        Assert.DoesNotContain(
            global.Claims,
            claim => claim.Type == RestaurantClaimTypes.Permission
        );

        ApplicationUser restaurantManager = User("manager@example.test", "Manager", Guid.NewGuid());
        IssuedAccessToken scopedToken = issuer.Issue(restaurantManager);
        JwtSecurityToken scoped = new JwtSecurityTokenHandler().ReadJwtToken(scopedToken.Value);
        Assert.Contains(scoped.Claims, claim => claim.Type == RestaurantClaimTypes.RestaurantId);
        Assert.Contains(
            scoped.Claims,
            claim =>
                claim.Type == RestaurantClaimTypes.RestaurantRole
                && claim.Value.EndsWith(":Manager", StringComparison.Ordinal)
        );
        Assert.Contains(scoped.Claims, claim => claim.Type == RestaurantClaimTypes.Permission);
        Assert.DoesNotContain(scoped.Claims, claim => claim.Type == ClaimTypes.Role);
    }

    [Fact]
    public void Dependency_registration_wires_identity_store_issuer_and_system_time()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IdentityWrite"] = "Host=localhost;Database=identity",
                }
            )
            .Build();

        services.AddIdentityInfrastructure(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IdentityWriteDbContext)
        );
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IIdentityStore)
                && descriptor.ImplementationType == typeof(IdentityStore)
                && descriptor.Lifetime == ServiceLifetime.Scoped
        );
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IAccessTokenIssuer)
                && descriptor.ImplementationType == typeof(AccessTokenService)
                && descriptor.Lifetime == ServiceLifetime.Scoped
        );
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(TimeProvider)
                && descriptor.Lifetime == ServiceLifetime.Singleton
        );
    }

    [Fact]
    public void Bootstrap_accounts_are_normalized_and_duplicate_emails_with_different_roles_fail()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentityBootstrap:AdminEmail"] = " ADMIN@example.test ; admin@example.test ",
                    ["IdentityBootstrap:GerenteEmail"] =
                        "gerencia@example.test, gerente2@example.test",
                    ["IdentityBootstrap:ContabilidadEmail"] = "conta@example.test",
                    ["IdentityBootstrap:MarketingEmail"] = "marketing@example.test",
                }
            )
            .Build();
        MethodInfo getAccounts = typeof(DependencyInjection).GetMethod(
            "GetBootstrapAccounts",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        object[] accounts = (
            (System.Collections.IEnumerable)getAccounts.Invoke(null, [configuration])!
        )
            .Cast<object>()
            .ToArray();

        Assert.Equal(5, accounts.Length);
        Assert.Contains(
            accounts,
            account =>
                (string)account.GetType().GetProperty("Email")!.GetValue(account)!
                    == "admin@example.test"
                && (string)account.GetType().GetProperty("Role")!.GetValue(account)! == "Admin"
        );

        IConfiguration conflicting = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentityBootstrap:AdminEmail"] = "same@example.test",
                    ["IdentityBootstrap:GerenteEmail"] = "same@example.test",
                }
            )
            .Build();
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() =>
            getAccounts.Invoke(null, [conflicting])
        );
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("same@example.test", failure.InnerException!.Message);
    }

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Google")]
    public void Active_provider_accepts_supported_external_auth_providers(string provider)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ExternalAuth:Provider"] = provider }
            )
            .Build();
        MethodInfo readProvider = typeof(DependencyInjection).GetMethod(
            "ReadActiveProvider",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        Assert.Equal(provider, readProvider.Invoke(null, [configuration]));
    }

    private static IdentityWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<IdentityWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static ApplicationUser User(string email, string role, Guid? restaurantId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            Provider = "Microsoft",
            ProviderSubject = email,
            TenantId = "tenant-1",
            DisplayName =
                role == "Admin" ? "Admin"
                : role == "Manager" ? "Manager"
                : "Inactive",
            Role = role,
            RestaurantId = restaurantId,
            AllRestaurants = restaurantId is null,
            IsActive = true,
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
