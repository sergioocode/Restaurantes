using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Identity.Api.Controllers;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Contracts.Requests;
using Restaurantes.Identity.Domain;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Identity.Application.Tests;

public sealed class IdentityControllerCoverageTests
{
    [Fact]
    public async Task Base_maps_each_identity_outcome_to_its_http_result()
    {
        ProbeController controller = new(Configuration());
        object value = new { id = Guid.NewGuid() };
        (IdentityOutcome Outcome, Type ResultType)[] cases =
        [
            (IdentityOutcome.Ok, typeof(OkObjectResult)),
            (IdentityOutcome.Created, typeof(CreatedResult)),
            (IdentityOutcome.NoContent, typeof(NoContentResult)),
            (IdentityOutcome.BadRequest, typeof(BadRequestObjectResult)),
            (IdentityOutcome.Unauthorized, typeof(UnauthorizedResult)),
            (IdentityOutcome.Forbidden, typeof(ForbidResult)),
            (IdentityOutcome.NotFound, typeof(NotFoundResult)),
            (IdentityOutcome.Conflict, typeof(ConflictObjectResult)),
        ];

        foreach ((IdentityOutcome outcome, Type resultType) in cases)
        {
            IActionResult result = await controller.Map(outcome, value, "/users/1");
            Assert.IsType(resultType, result);
        }

        CreatedResult created = Assert.IsType<CreatedResult>(
            await controller.Map(IdentityOutcome.Created, value, "/users/1")
        );
        Assert.Equal("/users/1", created.Location);
        Assert.Same(value, created.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Map((IdentityOutcome)100, value, null)
        );
    }

    [Fact]
    public void Base_reads_provider_configuration_and_rejects_an_unknown_active_provider()
    {
        ProbeController controller = new(Configuration("Microsoft"));

        Assert.Equal("Microsoft", controller.CurrentProvider);
        Assert.True(controller.IsMicrosoftConfigured);
        Assert.True(controller.IsGoogleConfigured);

        ProbeController google = new(Configuration("Google"));
        Assert.Equal("Google", google.CurrentProvider);
        ProbeController incomplete = new(Configuration("Other", configured: false));
        Assert.False(incomplete.IsMicrosoftConfigured);
        Assert.False(incomplete.IsGoogleConfigured);
        Assert.Throws<InvalidOperationException>(() =>
            _ = new ProbeController(Configuration("Other")).CurrentProvider
        );
    }

    [Fact]
    public void Base_resolves_global_and_scoped_identity_management_permissions()
    {
        Guid restaurantId = Guid.NewGuid();
        ProbeController controller = new(Configuration());
        controller.HttpContext.User = User(new Claim(ClaimTypes.Role, "Admin"));

        (bool allRestaurants, HashSet<Guid> restaurantIds) = controller.Scope();

        Assert.True(allRestaurants);
        Assert.Empty(restaurantIds);

        controller.HttpContext.User = User(
            new Claim(
                RestaurantClaimTypes.Permission,
                $"{restaurantId:N}:{RestaurantPermissions.IdentityManage}"
            ),
            new Claim(
                RestaurantClaimTypes.Permission,
                $"not-a-guid:{RestaurantPermissions.IdentityManage}"
            ),
            new Claim(ClaimTypes.Role, "Camarero")
        );
        (allRestaurants, restaurantIds) = controller.Scope();

        Assert.False(allRestaurants);
        Assert.Equal([restaurantId], restaurantIds);
    }

    [Fact]
    public async Task Read_and_write_controllers_reject_requests_without_management_scope()
    {
        IdentityReadController read = new(null!, Configuration());
        IdentityWriteController write = new(null!, Configuration());
        Attach(read);
        Attach(write);

        Assert.IsType<OkObjectResult>(read.Me());
        Assert.IsType<ForbidResult>(await read.ListUsers(Ct));
        Assert.IsType<ForbidResult>(
            await write.CreateUser(new("person@example.test", "Person", "Manager", false, null), Ct)
        );
        Assert.IsType<ForbidResult>(
            await write.UpdateUser(Guid.NewGuid(), new("Person", "Manager", false, null, true), Ct)
        );
        Assert.IsType<ForbidResult>(await write.DeleteUser(Guid.NewGuid(), Ct));

        Guid self = Guid.NewGuid();
        write.HttpContext.User = User(new Claim(ClaimTypes.NameIdentifier, self.ToString("D")));
        Assert.IsType<BadRequestObjectResult>(await write.DeleteUser(self, Ct));
        Assert.IsType<BadRequestResult>(await write.Start("Unknown", "/pos/", Ct));
        Assert.IsType<BadRequestResult>(await write.Start("Microsoft", "/external/", Ct));
        Assert.IsType<BadRequestResult>(await write.Complete("Unknown", "/pos/", Ct));
        Assert.IsType<BadRequestResult>(await write.Complete("Microsoft", "/external/", Ct));
    }

    [Fact]
    public async Task Start_signs_out_external_cookie_and_challenges_configured_provider()
    {
        FakeAuthenticationService authentication = new(AuthenticateResult.NoResult());
        IdentityService service = CreateIdentityService(new FakeIdentityStore());
        IdentityWriteController controller = CreateWriteController(
            service,
            Configuration(),
            authentication
        );

        IActionResult result = await controller.Start("Microsoft", "/pos/", Ct);

        ChallengeResult challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Equal(["Microsoft"], challenge.AuthenticationSchemes);
        Assert.Equal("Microsoft", challenge.Properties!.Items["provider"]);
        Assert.Equal("/pos/", challenge.Properties.Items["returnPath"]);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Fact]
    public async Task Complete_rejects_microsoft_identity_from_unconfigured_tenant()
    {
        ClaimsPrincipal principal = new(
            new ClaimsIdentity(
                [
                    new Claim("email", "person@example.test"),
                    new Claim("oid", "subject-1"),
                    new Claim("tid", "other-tenant"),
                ],
                "External"
            )
        );
        FakeAuthenticationService authentication = ExternalAuthentication("Microsoft", principal);
        IdentityService service = CreateIdentityService(new FakeIdentityStore());
        IdentityWriteController controller = CreateWriteController(
            service,
            Configuration(),
            authentication
        );

        IActionResult result = await controller.Complete("Microsoft", "/pos/", Ct);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/pos/#login_error=tenant", redirect.Url);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Fact]
    public async Task Complete_links_valid_microsoft_identity_and_redirects_with_login_code()
    {
        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            Email = "person@example.test",
            Provider = "Microsoft",
            DisplayName = "Person",
            Role = "Manager",
            IsActive = true,
        };
        FakeIdentityStore store = new() { User = user };
        ClaimsPrincipal principal = new(
            new ClaimsIdentity(
                [
                    new Claim("email", user.Email),
                    new Claim("oid", "subject-1"),
                    new Claim("tid", "tenant-id"),
                    new Claim("acct", "0"),
                ],
                "External"
            )
        );
        FakeAuthenticationService authentication = ExternalAuthentication("Microsoft", principal);
        IdentityService service = CreateIdentityService(store);
        IdentityWriteController controller = CreateWriteController(
            service,
            Configuration(),
            authentication
        );

        IActionResult result = await controller.Complete("Microsoft", "/pos/", Ct);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/pos/#login_code=login-code", redirect.Url);
        Assert.Equal("subject-1", user.ProviderSubject);
        Assert.Equal("tenant-id", user.TenantId);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Fact]
    public async Task Complete_rejects_missing_authentication_challenge_and_incomplete_identity()
    {
        ClaimsPrincipal principal = new(
            new ClaimsIdentity([new Claim("email", "person@example.test")], "External")
        );
        FakeAuthenticationService[] authentications =
        [
            new(AuthenticateResult.NoResult()),
            new(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        new AuthenticationProperties { Items = { ["provider"] = "Google" } },
                        "External"
                    )
                )
            ),
            ExternalAuthentication("Microsoft", principal),
        ];
        string[] expectedErrors = ["authentication", "authentication", "identity"];

        for (int index = 0; index < authentications.Length; index++)
        {
            IdentityWriteController controller = CreateWriteController(
                CreateIdentityService(new FakeIdentityStore()),
                Configuration(),
                authentications[index]
            );

            RedirectResult redirect = Assert.IsType<RedirectResult>(
                await controller.Complete("Microsoft", "/pos/", Ct)
            );

            Assert.Equal($"/pos/#login_error={expectedErrors[index]}", redirect.Url);
            Assert.Equal(1, authentications[index].SignOutCount);
        }
    }

    [Fact]
    public async Task Complete_checks_microsoft_account_type_and_google_workspace_before_linking()
    {
        ClaimsPrincipal personalMicrosoftAccount = new(
            new ClaimsIdentity(
                [
                    new Claim("email", "person@example.test"),
                    new Claim("oid", "subject-1"),
                    new Claim("tid", "tenant-id"),
                    new Claim("acct", "1"),
                ],
                "External"
            )
        );
        IdentityWriteController microsoft = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(),
            ExternalAuthentication("Microsoft", personalMicrosoftAccount)
        );
        RedirectResult microsoftRedirect = Assert.IsType<RedirectResult>(
            await microsoft.Complete("Microsoft", "/pos/", Ct)
        );
        Assert.Equal("/pos/#login_error=tenant", microsoftRedirect.Url);

        foreach (
            (string Domain, string Verified) in new[]
            {
                ("outside.test", "true"),
                ("example.test", "false"),
            }
        )
        {
            ClaimsPrincipal rejectedGoogle = GooglePrincipal(Domain, Verified);
            IdentityWriteController google = CreateWriteController(
                CreateIdentityService(new FakeIdentityStore()),
                Configuration("Google"),
                ExternalAuthentication("Google", rejectedGoogle)
            );

            RedirectResult redirect = Assert.IsType<RedirectResult>(
                await google.Complete("Google", "/pos/", Ct)
            );

            Assert.Equal("/pos/#login_error=workspace", redirect.Url);
        }

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            Email = "person@example.test",
            Provider = "Google",
            DisplayName = "Person",
            Role = "Manager",
            IsActive = true,
        };
        FakeIdentityStore store = new() { User = user };
        FakeAuthenticationService authentication = ExternalAuthentication(
            "Google",
            GooglePrincipal("example.test", "TRUE")
        );
        IdentityWriteController linked = CreateWriteController(
            CreateIdentityService(store),
            Configuration("Google"),
            authentication
        );

        RedirectResult linkedRedirect = Assert.IsType<RedirectResult>(
            await linked.Complete("Google", "/pos/", Ct)
        );

        Assert.Equal("/pos/#login_code=login-code", linkedRedirect.Url);
        Assert.Equal("google-subject", user.ProviderSubject);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, authentication.SignOutCount);
    }

    private static IConfiguration Configuration(
        string provider = "Microsoft",
        bool configured = true
    )
    {
        Dictionary<string, string?> values = new() { ["ExternalAuth:Provider"] = provider };
        if (configured)
        {
            values["ExternalAuth:PublicOrigin"] = "https://restaurant.example.test";
            values["ExternalAuth:Microsoft:TenantId"] = "tenant-id";
            values["ExternalAuth:Microsoft:ClientId"] = "microsoft-client";
            values["ExternalAuth:Microsoft:ClientSecret"] = "microsoft-secret";
            values["ExternalAuth:Google:WorkspaceDomain"] = "example.test";
            values["ExternalAuth:Google:ClientId"] = "google-client";
            values["ExternalAuth:Google:ClientSecret"] = "google-secret";
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static IdentityService CreateIdentityService(FakeIdentityStore store) =>
        new(store, new FakeTokenIssuer(), TimeProvider.System);

    private static IdentityWriteController CreateWriteController(
        IdentityService service,
        IConfiguration configuration,
        FakeAuthenticationService authentication
    )
    {
        IdentityWriteController controller = new(service, configuration);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton<IAuthenticationService>(authentication)
                    .BuildServiceProvider(),
            },
        };
        return controller;
    }

    private static FakeAuthenticationService ExternalAuthentication(
        string provider,
        ClaimsPrincipal principal
    ) =>
        new(
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    principal,
                    new AuthenticationProperties { Items = { ["provider"] = provider } },
                    "External"
                )
            )
        );

    private static ClaimsPrincipal GooglePrincipal(string domain, string emailVerified) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim("email", "person@example.test"),
                    new Claim("sub", "google-subject"),
                    new Claim("hd", domain),
                    new Claim("email_verified", emailVerified),
                ],
                "External"
            )
        );

    private static void Attach(ControllerBase controller) =>
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeAuthenticationService(AuthenticateResult result)
        : IAuthenticationService
    {
        public int SignOutCount { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(result);

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        )
        {
            SignOutCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdentityStore : IIdentityStore
    {
        public ApplicationUser? User { get; init; }
        public int Saves { get; private set; }

        public Task<AuthenticationSettings> GetSettingsAsync(CancellationToken ct) =>
            Task.FromResult(new AuthenticationSettings());

        public Task<ApplicationUser?> FindByEmailAsync(
            string provider,
            string email,
            CancellationToken ct
        ) =>
            Task.FromResult(
                User is { Provider: var currentProvider, Email: var currentEmail }
                && currentProvider == provider
                && currentEmail == email
                    ? User
                    : null
            );

        public Task<ApplicationUser?> FindBySubjectAsync(
            string provider,
            string? tenantId,
            string subject,
            CancellationToken ct
        ) => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(User?.Id == id ? User : null);

        public Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct) =>
            Task.FromResult(User is null ? new List<ApplicationUser>() : [User]);

        public Task<bool> AnyActiveAdminForProviderAsync(string provider, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> AnyActiveAdminExceptAsync(
            Guid id,
            string provider,
            CancellationToken ct
        ) => Task.FromResult(false);

        public Task AddUserAsync(ApplicationUser user, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteUserAsync(ApplicationUser user, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task<string> CreateLoginTicketAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult("login-code");

        public Task<ApplicationUser?> ConsumeLoginTicketAsync(string code, CancellationToken ct) =>
            Task.FromResult(User);
    }

    private sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(ApplicationUser user) =>
            new("token", DateTime.UtcNow.AddHours(1));
    }

    private sealed class ProbeController : IdentityControllerBase
    {
        public ProbeController(IConfiguration configuration)
            : base(configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }

        public string CurrentProvider => ActiveProvider;
        public bool IsMicrosoftConfigured => MicrosoftConfigured;
        public bool IsGoogleConfigured => GoogleConfigured;

        public (bool AllRestaurants, HashSet<Guid> RestaurantIds) Scope() =>
            IdentityManagementScope();

        public Task<IActionResult> Map(IdentityOutcome outcome, object? value, string? location) =>
            Execute(() => Task.FromResult(new IdentityResult(outcome, value, location)));
    }
}
