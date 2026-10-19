using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Api.Controllers;
using Restaurantes.CashRegister.Contracts.Requests;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterWriteControllerAuthorizationTests
{
    [Fact]
    public async Task Open_forbids_user_without_cash_register_manage_permission()
    {
        CashRegisterWriteController controller = CreateController();

        IActionResult result = await controller.Open(
            Guid.NewGuid(),
            new OpenCashRegisterRequest(100m),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Close_forbids_user_without_cash_register_manage_permission()
    {
        CashRegisterWriteController controller = CreateController();

        IActionResult result = await controller.Close(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CloseCashRegisterRequest([]),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateMovement_forbids_user_without_cash_register_manage_permission()
    {
        CashRegisterWriteController controller = CreateController();

        IActionResult result = await controller.CreateMovement(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateCashMovementRequest("income", "Cash", 10m, "Other", "test"),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
    }

    private static CashRegisterWriteController CreateController()
    {
        return new(null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
