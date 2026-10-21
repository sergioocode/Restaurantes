using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Identity.Api.Controllers;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Contracts.Requests;
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

    private static void Attach(ControllerBase controller) =>
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
