using Restaurantes.Identity.Application;
using Restaurantes.Identity.Contracts.Requests;
using Restaurantes.Identity.Contracts.Responses;
using Restaurantes.Identity.Domain;
using Xunit;

namespace Restaurantes.Identity.Application.Tests;

public sealed class IdentityServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Provider_settings_reads_store_and_returns_configured_providers()
    {
        FakeIdentityStore store = new();
        IdentityService service = CreateService(store);

        IdentityResult result = await service.GetProviderSettings("Microsoft", true, false, Ct);

        Assert.Equal(IdentityOutcome.Ok, result.Outcome);
        Assert.Equal(new ProviderSettingsResponse("Microsoft", true, false), result.Value);
        Assert.Equal(1, store.SettingsReads);
    }

    [Fact]
    public async Task Create_user_validates_fields_scope_and_duplicate_email()
    {
        FakeIdentityStore store = new();
        IdentityService service = CreateService(store);
        Guid restaurantId = Guid.NewGuid();

        IdentityResult invalidEmail = await service.CreateUser(
            "Microsoft",
            true,
            Set(restaurantId),
            new("not-an-email", "Name", "Manager", false, restaurantId),
            Ct
        );
        IdentityResult invalidRole = await service.CreateUser(
            "Microsoft",
            true,
            Set(restaurantId),
            new("person@example.test", "Name", "Admin", false, restaurantId),
            Ct
        );
        IdentityResult invalidGlobalScope = await service.CreateUser(
            "Microsoft",
            true,
            Set(restaurantId),
            new("person@example.test", "Name", "Manager", true, restaurantId),
            Ct
        );
        IdentityResult forbidden = await service.CreateUser(
            "Microsoft",
            false,
            Set(Guid.NewGuid()),
            new("person@example.test", "Name", "Manager", false, restaurantId),
            Ct
        );
        store.Users.Add(User("person@example.test", restaurantId: restaurantId));
        IdentityResult duplicate = await service.CreateUser(
            "Microsoft",
            true,
            Set(restaurantId),
            new(" PERSON@example.test ", "Name", "Manager", false, restaurantId),
            Ct
        );

        Assert.Equal(IdentityOutcome.BadRequest, invalidEmail.Outcome);
        Assert.Equal(IdentityOutcome.BadRequest, invalidRole.Outcome);
        Assert.Equal(IdentityOutcome.BadRequest, invalidGlobalScope.Outcome);
        Assert.Equal(IdentityOutcome.Forbidden, forbidden.Outcome);
        Assert.Equal(IdentityOutcome.Conflict, duplicate.Outcome);
        Assert.Empty(store.AddedUsers);
    }

    [Fact]
    public async Task Create_user_normalizes_email_and_display_name_and_uses_utc_time()
    {
        FakeIdentityStore store = new();
        IdentityService service = CreateService(store);
        Guid restaurantId = Guid.NewGuid();

        IdentityResult result = await service.CreateUser(
            "Microsoft",
            false,
            Set(restaurantId),
            new("  PERSON@Example.Test ", "  Ana Pérez  ", "Camarero", false, restaurantId),
            Ct
        );

        AccountResponse account = Assert.IsType<AccountResponse>(result.Value);
        Assert.Equal(IdentityOutcome.Created, result.Outcome);
        Assert.Equal("person@example.test", account.Email);
        Assert.Equal("Ana Pérez", account.DisplayName);
        Assert.Equal("Microsoft", account.Provider);
        Assert.Equal("Camarero", account.Role);
        Assert.Equal(restaurantId, account.RestaurantId);
        Assert.True(account.IsActive);
        Assert.False(account.IsLinked);
        Assert.Equal(Now, Assert.Single(store.AddedUsers).CreatedAtUtc);
        Assert.Equal($"/api/identity/users/{account.Id}", result.Location);
    }

    [Fact]
    public async Task List_users_returns_only_managed_supported_roles()
    {
        Guid managedRestaurant = Guid.NewGuid();
        FakeIdentityStore store = new();
        store.Users.AddRange([
            User("manager@example.test", "Manager", managedRestaurant),
            User("other@example.test", "Kds", Guid.NewGuid()),
            User("admin@example.test", "Admin", managedRestaurant),
            User("global@example.test", "Camarero", allRestaurants: true),
        ]);
        IdentityService service = CreateService(store);

        IdentityResult result = await service.ListUsers(false, Set(managedRestaurant), Ct);

        AccountResponse[] users = Assert.IsType<AccountResponse[]>(result.Value);
        Assert.Equal(["manager@example.test"], users.Select(user => user.Email));
        Assert.Equal(IdentityOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Update_user_checks_existence_scope_validation_and_last_admin()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeIdentityStore store = new();
        ApplicationUser user = User("admin@example.test", "Admin", restaurantId);
        store.Users.Add(user);
        IdentityService service = CreateService(store);

        IdentityResult missing = await service.UpdateUser(
            Guid.NewGuid(),
            true,
            Set(restaurantId),
            new("Admin", "Admin", false, restaurantId, true),
            Ct
        );
        IdentityResult forbidden = await service.UpdateUser(
            user.Id,
            false,
            Set(Guid.NewGuid()),
            new("Admin", "Admin", false, restaurantId, true),
            Ct
        );
        IdentityResult invalid = await service.UpdateUser(
            user.Id,
            true,
            Set(restaurantId),
            new(" ", "Manager", false, restaurantId, true),
            Ct
        );
        IdentityResult lastAdmin = await service.UpdateUser(
            user.Id,
            true,
            Set(restaurantId),
            new("Admin", "Manager", false, restaurantId, true),
            Ct
        );

        Assert.Equal(IdentityOutcome.NotFound, missing.Outcome);
        Assert.Equal(IdentityOutcome.Forbidden, forbidden.Outcome);
        Assert.Equal(IdentityOutcome.BadRequest, invalid.Outcome);
        Assert.Equal(IdentityOutcome.BadRequest, lastAdmin.Outcome);
        Assert.Equal("Admin", user.Role);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Update_user_persists_valid_changes_when_another_admin_exists()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeIdentityStore store = new() { HasOtherActiveAdmin = true };
        ApplicationUser user = User("admin@example.test", "Admin", restaurantId);
        store.Users.Add(user);
        IdentityService service = CreateService(store);

        IdentityResult result = await service.UpdateUser(
            user.Id,
            true,
            Set(restaurantId),
            new("  Ana  ", "Manager", false, restaurantId, false),
            Ct
        );

        AccountResponse account = Assert.IsType<AccountResponse>(result.Value);
        Assert.Equal(IdentityOutcome.Ok, result.Outcome);
        Assert.Equal("Ana", account.DisplayName);
        Assert.Equal("Manager", user.Role);
        Assert.False(user.IsActive);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Delete_user_checks_existence_scope_and_protects_last_admin()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeIdentityStore store = new();
        ApplicationUser admin = User("admin@example.test", "Admin", restaurantId);
        ApplicationUser manager = User("manager@example.test", "Manager", restaurantId);
        store.Users.AddRange([admin, manager]);
        IdentityService service = CreateService(store);

        IdentityResult missing = await service.DeleteUser(Guid.NewGuid(), true, Set(), Ct);
        IdentityResult forbidden = await service.DeleteUser(
            manager.Id,
            false,
            Set(Guid.NewGuid()),
            Ct
        );
        IdentityResult lastAdmin = await service.DeleteUser(admin.Id, true, Set(), Ct);
        IdentityResult deleted = await service.DeleteUser(manager.Id, true, Set(), Ct);

        Assert.Equal(IdentityOutcome.NotFound, missing.Outcome);
        Assert.Equal(IdentityOutcome.Forbidden, forbidden.Outcome);
        Assert.Equal(IdentityOutcome.BadRequest, lastAdmin.Outcome);
        Assert.Equal(IdentityOutcome.NoContent, deleted.Outcome);
        Assert.Same(manager, Assert.Single(store.DeletedUsers));
    }

    [Fact]
    public async Task External_authorization_links_subject_and_rejects_provider_subject_conflicts()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeIdentityStore store = new();
        ApplicationUser user = User("person@example.test", "Manager", restaurantId);
        store.Users.Add(user);
        IdentityService service = CreateService(store);

        IdentityResult wrongProvider = await service.AuthorizeExternalAsync(
            "Google",
            "Microsoft",
            user.Email,
            "subject-1",
            "tenant-1",
            Ct
        );
        IdentityResult linked = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            " PERSON@EXAMPLE.TEST ",
            "subject-1",
            "tenant-1",
            Ct
        );
        IdentityResult changedSubject = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            user.Email,
            "subject-2",
            "tenant-1",
            Ct
        );

        Assert.Equal(IdentityOutcome.Unauthorized, wrongProvider.Outcome);
        Assert.Equal(IdentityOutcome.Ok, linked.Outcome);
        Assert.Equal("subject-1", user.ProviderSubject);
        Assert.Equal("tenant-1", user.TenantId);
        Assert.Equal(1, store.Saves);
        Assert.Equal("login-code", linked.Value);
        Assert.Equal(IdentityOutcome.Unauthorized, changedSubject.Outcome);
    }

    [Fact]
    public async Task External_authorization_rejects_unknown_inactive_and_already_claimed_accounts()
    {
        Guid restaurantId = Guid.NewGuid();
        ApplicationUser inactive = User(
            "inactive@example.test",
            "Manager",
            restaurantId,
            active: false
        );
        ApplicationUser target = User("target@example.test", "Manager", restaurantId);
        ApplicationUser subjectOwner = User("owner@example.test", "Manager", restaurantId);
        subjectOwner.ProviderSubject = "claimed-subject";
        subjectOwner.TenantId = "tenant";
        FakeIdentityStore store = new();
        store.Users.AddRange([inactive, target, subjectOwner]);
        IdentityService service = CreateService(store);

        IdentityResult unknown = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            "missing@example.test",
            "subject",
            "tenant",
            Ct
        );
        IdentityResult disabled = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            inactive.Email,
            "subject",
            "tenant",
            Ct
        );
        IdentityResult claimed = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            target.Email,
            "claimed-subject",
            "tenant",
            Ct
        );
        IdentityResult linked = await service.AuthorizeExternalAsync(
            "Microsoft",
            "Microsoft",
            subjectOwner.Email,
            "claimed-subject",
            "tenant",
            Ct
        );

        Assert.Equal(IdentityOutcome.Unauthorized, unknown.Outcome);
        Assert.Equal(IdentityOutcome.Unauthorized, disabled.Outcome);
        Assert.Equal(IdentityOutcome.Unauthorized, claimed.Outcome);
        Assert.Equal(IdentityOutcome.Ok, linked.Outcome);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Exchange_rejects_invalid_user_and_returns_restaurant_permissions()
    {
        Guid restaurantId = Guid.NewGuid();
        FakeIdentityStore store = new();
        FakeTokenIssuer tokens = new();
        IdentityService service = CreateService(store, tokens);
        store.TicketUser = User("inactive@example.test", "Manager", restaurantId, active: false);
        IdentityResult inactive = await service.ExchangeAsync("Microsoft", "code", Ct);
        store.TicketUser = User("person@example.test", "Camarero", restaurantId);
        IdentityResult wrongProvider = await service.ExchangeAsync("Google", "code", Ct);
        IdentityResult exchanged = await service.ExchangeAsync("Microsoft", "code", Ct);

        LoginResponse login = Assert.IsType<LoginResponse>(exchanged.Value);
        Assert.Equal(IdentityOutcome.Unauthorized, inactive.Outcome);
        Assert.Equal(IdentityOutcome.Unauthorized, wrongProvider.Outcome);
        Assert.Equal(IdentityOutcome.Ok, exchanged.Outcome);
        Assert.Equal("access-token", login.AccessToken);
        Assert.Equal("Bearer", login.TokenType);
        Assert.Equal("person@example.test", login.User.Username);
        LoginRestaurantResponse restaurant = Assert.Single(login.Restaurants);
        Assert.Equal(restaurantId, restaurant.RestaurantId);
        Assert.Contains("commander.use", restaurant.Permissions);
        Assert.Empty(login.AllRestaurantsRoles);
        Assert.Equal(1, tokens.Issued);
    }

    [Fact]
    public async Task Exchange_returns_all_restaurant_role_and_permissions_for_global_admin()
    {
        FakeIdentityStore store = new()
        {
            TicketUser = User("admin@example.test", "Admin", allRestaurants: true),
        };
        IdentityService service = CreateService(store);

        IdentityResult result = await service.ExchangeAsync("Microsoft", "code", Ct);

        LoginResponse login = Assert.IsType<LoginResponse>(result.Value);
        Assert.Equal(IdentityOutcome.Ok, result.Outcome);
        Assert.Equal(["Admin"], login.AllRestaurantsRoles);
        Assert.Contains("identity.manage", login.AllRestaurantsPermissions);
        Assert.Empty(login.Restaurants);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IdentityService CreateService(
        FakeIdentityStore? store = null,
        FakeTokenIssuer? tokens = null
    ) => new(store ?? new(), tokens ?? new(), new FixedTimeProvider(Now));

    private static HashSet<Guid> Set(params Guid[] ids) => ids.ToHashSet();

    private static ApplicationUser User(
        string email,
        string role = "Manager",
        Guid? restaurantId = null,
        bool allRestaurants = false,
        bool active = true
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            Provider = "Microsoft",
            DisplayName = email,
            Role = role,
            RestaurantId = restaurantId,
            AllRestaurants = allRestaurants,
            IsActive = active,
            CreatedAtUtc = Now,
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public int Issued { get; private set; }

        public IssuedAccessToken Issue(ApplicationUser user)
        {
            Issued++;
            return new("access-token", Now.AddHours(1));
        }
    }

    private sealed class FakeIdentityStore : IIdentityStore
    {
        public List<ApplicationUser> Users { get; } = [];
        public List<ApplicationUser> AddedUsers { get; } = [];
        public List<ApplicationUser> DeletedUsers { get; } = [];
        public ApplicationUser? TicketUser { get; set; }
        public bool HasOtherActiveAdmin { get; set; }
        public int Saves { get; private set; }
        public int SettingsReads { get; private set; }

        public Task<AuthenticationSettings> GetSettingsAsync(CancellationToken ct)
        {
            SettingsReads++;
            return Task.FromResult(new AuthenticationSettings());
        }

        public Task<ApplicationUser?> FindByEmailAsync(
            string provider,
            string email,
            CancellationToken ct
        ) =>
            Task.FromResult(
                Users.FirstOrDefault(user => user.Provider == provider && user.Email == email)
            );

        public Task<ApplicationUser?> FindBySubjectAsync(
            string provider,
            string? tenantId,
            string subject,
            CancellationToken ct
        ) =>
            Task.FromResult(
                Users.FirstOrDefault(user =>
                    user.Provider == provider
                    && user.TenantId == tenantId
                    && user.ProviderSubject == subject
                )
            );

        public Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Users.FirstOrDefault(user => user.Id == id));

        public Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct) =>
            Task.FromResult(Users);

        public Task<bool> AnyActiveAdminForProviderAsync(string provider, CancellationToken ct) =>
            Task.FromResult(HasOtherActiveAdmin);

        public Task<bool> AnyActiveAdminExceptAsync(
            Guid id,
            string provider,
            CancellationToken ct
        ) => Task.FromResult(HasOtherActiveAdmin);

        public Task AddUserAsync(ApplicationUser user, CancellationToken ct)
        {
            AddedUsers.Add(user);
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(ApplicationUser user, CancellationToken ct)
        {
            DeletedUsers.Add(user);
            Users.Remove(user);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task<string> CreateLoginTicketAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult("login-code");

        public Task<ApplicationUser?> ConsumeLoginTicketAsync(string code, CancellationToken ct) =>
            Task.FromResult(TicketUser);
    }
}
