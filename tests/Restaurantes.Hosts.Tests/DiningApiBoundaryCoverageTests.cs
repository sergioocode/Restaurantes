using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Dining.Api;
using Restaurantes.Dining.Application;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningApiBoundaryCoverageTests
{
    [Fact]
    public void Dining_authorization_maps_each_domain_permission_to_its_scoped_claim()
    {
        Guid restaurantId = Guid.NewGuid();
        (DiningPermission Permission, string Claim)[] mappings =
        [
            (DiningPermission.TablesRead, RestaurantPermissions.TablesRead),
            (DiningPermission.KdsZonesRead, RestaurantPermissions.KdsUse),
            (DiningPermission.TablesManage, RestaurantPermissions.TablesManage),
            (DiningPermission.OrdersCreate, RestaurantPermissions.OrdersCreate),
            (DiningPermission.TablesRelease, RestaurantPermissions.TablesRelease),
            (DiningPermission.TablesReleasePaid, RestaurantPermissions.TablesReleasePaid),
            (DiningPermission.PaymentsCapture, RestaurantPermissions.PaymentsCapture),
            (DiningPermission.PaymentsCaptureCard, RestaurantPermissions.PaymentsCaptureCard),
            (DiningPermission.DiscountsApply, RestaurantPermissions.DiscountsApply),
        ];
        ClaimsPrincipal principal = new(
            new ClaimsIdentity(
                mappings
                    .Select(mapping => new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(restaurantId, mapping.Claim)
                    ))
                    .ToArray(),
                "test"
            )
        );
        DiningAuthorization authorization = new();

        foreach ((DiningPermission permission, _) in mappings)
        {
            Assert.True(authorization.CanAccessRestaurant(principal, restaurantId, permission));
            Assert.False(authorization.CanAccessRestaurant(principal, Guid.NewGuid(), permission));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            authorization.CanAccessRestaurant(principal, restaurantId, (DiningPermission)100)
        );
    }

    [Fact]
    public void Request_context_reads_customer_and_authorization_headers()
    {
        Guid userId = Guid.NewGuid();
        DefaultHttpContext http = new();
        http.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")
        );
        http.Request.Headers["X-Customer-Session-Token"] = "qr-token";
        http.Request.Headers.Authorization = "Bearer access-token";

        DiningRequest request = (DiningRequest)InvokeRequestContext("FromHttpContext", http)!;

        Assert.Equal(userId.ToString(), request.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("qr-token", request.CustomerSessionToken);
        Assert.Equal("Bearer access-token", request.Authorization);
    }

    [Fact]
    public void Request_context_maps_all_outcomes_and_adds_optional_response_headers()
    {
        object payload = new { id = Guid.NewGuid() };
        (DiningOutcome Outcome, Type ResultType)[] cases =
        [
            (DiningOutcome.Ok, typeof(OkObjectResult)),
            (DiningOutcome.Created, typeof(CreatedResult)),
            (DiningOutcome.NoContent, typeof(NoContentResult)),
            (DiningOutcome.BadRequest, typeof(BadRequestObjectResult)),
            (DiningOutcome.Unauthorized, typeof(UnauthorizedResult)),
            (DiningOutcome.Forbidden, typeof(ForbidResult)),
            (DiningOutcome.NotFound, typeof(NotFoundObjectResult)),
            (DiningOutcome.Conflict, typeof(ConflictObjectResult)),
            (DiningOutcome.Problem, typeof(ObjectResult)),
        ];

        foreach ((DiningOutcome outcome, Type resultType) in cases)
        {
            ProbeController controller = new();
            DiningResult result = new(
                outcome,
                payload,
                "/resource/1",
                GuestCount: 12,
                ProblemStatusCode: StatusCodes.Status422UnprocessableEntity,
                ProblemTitle: "Invalid dining operation",
                ProblemDetail: "The requested transition is not allowed.",
                ZoneId: Guid.Parse("923c7197-494f-4db8-90f8-73a7d54d3a35")
            );

            IActionResult action = (IActionResult)
                InvokeRequestContext("ToActionResult", controller, result)!;

            Assert.IsType(resultType, action);
            Assert.Equal("12", controller.Response.Headers["X-Dining-Guest-Count"]);
            Assert.Equal(
                "923c7197-494f-4db8-90f8-73a7d54d3a35",
                controller.Response.Headers["X-Dining-Zone-Id"]
            );
            if (action is CreatedResult created)
            {
                Assert.Equal("/resource/1", created.Location);
                Assert.Same(payload, created.Value);
            }
            if (action is ObjectResult problem && outcome == DiningOutcome.Problem)
            {
                Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
                ProblemDetails details = Assert.IsType<ProblemDetails>(problem.Value);
                Assert.Equal("Invalid dining operation", details.Title);
                Assert.Equal("The requested transition is not allowed.", details.Detail);
            }
        }

        ProbeController noHeadersController = new();
        _ = InvokeRequestContext(
            "ToActionResult",
            noHeadersController,
            new DiningResult(DiningOutcome.NoContent)
        );
        Assert.False(noHeadersController.Response.Headers.ContainsKey("X-Dining-Guest-Count"));
        Assert.False(noHeadersController.Response.Headers.ContainsKey("X-Dining-Zone-Id"));

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeRequestContext(
                "ToActionResult",
                new ProbeController(),
                new DiningResult((DiningOutcome)100)
            )
        );
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static object? InvokeRequestContext(string method, params object[] arguments)
    {
        Type type = typeof(DiningAuthorization).Assembly.GetType(
            "Restaurantes.Dining.Api.DiningRequestContext",
            throwOnError: true
        )!;
        MethodInfo target = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public)!;
        return target.Invoke(null, arguments);
    }

    private sealed class ProbeController : ControllerBase
    {
        public ProbeController()
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }
    }
}
