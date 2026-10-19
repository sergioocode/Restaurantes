using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Api.Controllers;
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
}
