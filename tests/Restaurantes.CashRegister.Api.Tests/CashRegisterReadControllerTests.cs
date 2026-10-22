using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Api.Controllers;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterReadControllerTests
{
    [Fact]
    public async Task Current_forbids_unauthenticated_user()
    {
        CashRegisterReadController controller = new(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        IActionResult result = await controller.Current(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task History_forbids_user_without_cash_register_permission()
    {
        CashRegisterReadController controller = new(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        IActionResult result = await controller.History(
            Guid.NewGuid(),
            null,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Is_open_is_public_and_maps_the_availability_response()
    {
        ICashRegisterStore store = DispatchProxy.Create<ICashRegisterStore, CashStoreProxy>();
        CashRegisterReadController controller = new(CreateService(store))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        IActionResult result = await controller.IsOpen(Guid.NewGuid(), CancellationToken.None);

        OkObjectResult response = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<CashRegisterAvailabilityResponse>(response.Value);
    }

    [Fact]
    public async Task Current_requires_scoped_management_permission_and_maps_absence_to_204()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterReadController forbidden = new(null!)
        {
            ControllerContext = Context(
                Permission(restaurantId, RestaurantPermissions.CashRegisterRead)
            ),
        };
        ICashRegisterStore store = DispatchProxy.Create<ICashRegisterStore, CashStoreProxy>();
        CashRegisterReadController allowed = new(CreateService(store))
        {
            ControllerContext = Context(
                Permission(restaurantId, RestaurantPermissions.CashRegisterManage)
            ),
        };

        Assert.IsType<ForbidResult>(await forbidden.Current(restaurantId, CancellationToken.None));
        Assert.IsType<NoContentResult>(await allowed.Current(restaurantId, CancellationToken.None));
    }

    [Fact]
    public async Task History_accepts_scoped_read_permission_and_returns_the_service_result()
    {
        Guid restaurantId = Guid.NewGuid();
        ICashRegisterStore store = DispatchProxy.Create<ICashRegisterStore, CashStoreProxy>();
        CashRegisterReadController controller = new(CreateService(store))
        {
            ControllerContext = Context(
                Permission(restaurantId, RestaurantPermissions.CashRegisterRead)
            ),
        };

        IActionResult result = await controller.History(
            restaurantId,
            new DateOnly(2026, 10, 22),
            CancellationToken.None
        );

        OkObjectResult response = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(
            Assert.IsType<System.Collections.IEnumerable>(response.Value, exactMatch: false)
        );
    }

    private static CashRegisterService CreateService(ICashRegisterStore store)
    {
        IIncompleteOrdersChecker checker = DispatchProxy.Create<
            IIncompleteOrdersChecker,
            IncompleteOrdersCheckerProxy
        >();
        return new(store, checker, TimeProvider.System, TimeZoneInfo.Utc);
    }

    private static ControllerContext Context(Claim claim) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([claim], "test")),
            },
        };

    private static Claim Permission(Guid restaurantId, string permission) =>
        new(RestaurantClaimTypes.Permission, $"{restaurantId:N}:{permission}");

    public class CashStoreProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(ICashRegisterStore.GetOpenSessionAsync) =>
                    Task.FromResult<CashRegisterSession?>(null),
                nameof(ICashRegisterStore.GetCurrentAsync) => Task.FromResult<CashRegisterSession?>(
                    null
                ),
                nameof(ICashRegisterStore.GetHistoryAsync) => Task.FromResult(
                    new List<CashRegisterSession>()
                ),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public class IncompleteOrdersCheckerProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
