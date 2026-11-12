using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
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
    public void Base_reports_whether_Keycloak_is_configured()
    {
        Assert.True(new ProbeController(Configuration()).IsKeycloakConfigured);
        Assert.False(new ProbeController(Configuration(configured: false)).IsKeycloakConfigured);
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
        Assert.IsType<BadRequestResult>(await write.Start("/external/"));
        Assert.IsType<BadRequestResult>(await write.Complete("/external/", Ct));
    }

    [Fact]
    public async Task Start_signs_out_external_cookie_and_challenges_Keycloak()
    {
        FakeAuthenticationService authentication = new(AuthenticateResult.NoResult());
        IdentityWriteController controller = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(),
            authentication
        );

        IActionResult result = await controller.Start("/pos/", reauthenticate: true);

        ChallengeResult challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Equal(["Keycloak"], challenge.AuthenticationSchemes);
        Assert.Equal("/pos/", challenge.Properties!.Items["returnPath"]);
        Assert.Equal("true", challenge.Properties.Items["reauthenticate"]);
        Assert.EndsWith("returnPath=%2Fpos%2F", challenge.Properties.RedirectUri);
        Assert.Equal(1, authentication.SignOutCount);

        IdentityWriteController unconfigured = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(configured: false),
            new FakeAuthenticationService(AuthenticateResult.NoResult())
        );
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<ObjectResult>(await unconfigured.Start("/pos/")).StatusCode
        );
    }

    [Fact]
    public void Logout_signs_out_local_and_Keycloak_sessions_for_an_allowed_return_path()
    {
        IdentityWriteController controller = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(),
            new FakeAuthenticationService(AuthenticateResult.NoResult())
        );

        SignOutResult result = Assert.IsType<SignOutResult>(controller.Logout("/kds/"));

        Assert.Equal(["External", "Keycloak"], result.AuthenticationSchemes);
        Assert.Equal("/kds/", result.Properties!.RedirectUri);
        Assert.IsType<BadRequestResult>(controller.Logout("//evil.example/"));

        IdentityWriteController unconfigured = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(configured: false),
            new FakeAuthenticationService(AuthenticateResult.NoResult())
        );
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<ObjectResult>(unconfigured.Logout("/kds/")).StatusCode
        );
    }

    [Fact]
    public async Task Complete_links_verified_Keycloak_identity_and_redirects_with_login_code()
    {
        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            Email = "person@example.test",
            Provider = "Keycloak",
            DisplayName = "Person",
            Role = "Manager",
            IsActive = true,
        };
        FakeIdentityStore store = new() { User = user };
        ClaimsPrincipal principal = KeycloakPrincipal(user.Email, "subject-1", "true");
        FakeAuthenticationService authentication = ExternalAuthentication("/pos/", principal);
        IdentityWriteController controller = CreateWriteController(
            CreateIdentityService(store),
            Configuration(),
            authentication
        );

        IActionResult result = await controller.Complete("/pos/", Ct);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/pos/#login_code=login-code", redirect.Url);
        Assert.Equal("subject-1", user.ProviderSubject);
        Assert.Null(user.TenantId);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Fact]
    public async Task Complete_rejects_unverified_email_missing_authentication_and_mismatched_return_path()
    {
        ClaimsPrincipal unverified = KeycloakPrincipal("person@example.test", "subject-1", "false");
        FakeAuthenticationService[] authentications =
        [
            new(AuthenticateResult.NoResult()),
            ExternalAuthentication(
                "/commander/",
                KeycloakPrincipal("person@example.test", "subject-1", "true")
            ),
            ExternalAuthentication("/pos/", unverified),
        ];
        string[] returnPaths = ["/pos/", "/pos/", "/pos/"];
        string[] expectedErrors = ["authentication", "authentication", "identity"];

        for (int index = 0; index < authentications.Length; index++)
        {
            IdentityWriteController controller = CreateWriteController(
                CreateIdentityService(new FakeIdentityStore()),
                Configuration(),
                authentications[index]
            );

            RedirectResult redirect = Assert.IsType<RedirectResult>(
                await controller.Complete(returnPaths[index], Ct)
            );

            Assert.Equal($"/pos/#login_error={expectedErrors[index]}", redirect.Url);
            Assert.Equal(1, authentications[index].SignOutCount);
        }
    }

    [Fact]
    public async Task Complete_uses_local_fallback_when_url_helper_rejects_return_path()
    {
        IdentityWriteController controller = CreateWriteController(
            CreateIdentityService(new FakeIdentityStore()),
            Configuration(),
            new FakeAuthenticationService(AuthenticateResult.NoResult())
        );
        controller.Url = new FixedUrlHelper(isLocal: false);

        RedirectResult redirect = Assert.IsType<RedirectResult>(
            await controller.Complete("/pos/", Ct)
        );

        Assert.Equal("/#login_error=authentication", redirect.Url);
    }

    private static IConfiguration Configuration(bool configured = true)
    {
        Dictionary<string, string?> values = [];
        if (configured)
        {
            values["ExternalAuth:PublicOrigin"] = "https://restaurant.example.test";
            values["ExternalAuth:Keycloak:Authority"] =
                "https://keycloak.example.test/realms/restaurants";
            values["ExternalAuth:Keycloak:ClientId"] = "identity-client";
            values["ExternalAuth:Keycloak:ClientSecret"] = "identity-secret";
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
        controller.Url = new FixedUrlHelper(isLocal: true);
        return controller;
    }

    private sealed class FixedUrlHelper(bool isLocal) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();

        public string? Action(UrlActionContext actionContext) => throw new NotSupportedException();

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => isLocal;

        public string? Link(string? routeName, object? values) => throw new NotSupportedException();

        public string? RouteUrl(UrlRouteContext routeContext) => throw new NotSupportedException();
    }

    private static FakeAuthenticationService ExternalAuthentication(
        string returnPath,
        ClaimsPrincipal principal
    ) =>
        new(
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    principal,
                    new AuthenticationProperties { Items = { ["returnPath"] = returnPath } },
                    "External"
                )
            )
        );

    private static ClaimsPrincipal KeycloakPrincipal(
        string email,
        string subject,
        string emailVerified
    ) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim("email", email),
                    new Claim("sub", subject),
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

        public bool IsKeycloakConfigured => KeycloakConfigured;

        public (bool AllRestaurants, HashSet<Guid> RestaurantIds) Scope() =>
            IdentityManagementScope();

        public Task<IActionResult> Map(IdentityOutcome outcome, object? value, string? location) =>
            Execute(() => Task.FromResult(new IdentityResult(outcome, value, location)));
    }
}
