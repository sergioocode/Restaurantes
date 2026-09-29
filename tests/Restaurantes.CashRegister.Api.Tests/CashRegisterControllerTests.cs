using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Api.Controllers;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterControllerTests
{
    [Fact]
    public async Task Current_forbids_unauthenticated_user()
    {
        CashRegisterController controller = new(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        IActionResult result = await controller.Current(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }
}
