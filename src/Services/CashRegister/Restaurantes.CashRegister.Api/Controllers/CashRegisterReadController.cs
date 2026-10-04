using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Application;
using Restaurantes.Security;

namespace Restaurantes.CashRegister.Api.Controllers;

[ApiController, Authorize, Route("api/cash-register/restaurants/{restaurantId:guid}")]
public sealed class CashRegisterReadController(CashRegisterService service) : ControllerBase
{
    [AllowAnonymous, HttpGet("is-open")]
    public Task<IActionResult> IsOpen(Guid restaurantId, CancellationToken ct)
    {
        return Execute(() => service.IsOpen(restaurantId, ct));
    }

    [HttpGet("current")]
    public Task<IActionResult> Current(Guid restaurantId, CancellationToken ct)
    {
        return CanManage(restaurantId)
            ? Execute(() => service.Current(restaurantId, ct))
            : Task.FromResult<IActionResult>(Forbid());
    }

    [HttpGet("history")]
    public Task<IActionResult> History(Guid restaurantId, CancellationToken ct)
    {
        return CanManage(restaurantId)
            ? Execute(() => service.History(restaurantId, ct))
            : Task.FromResult<IActionResult>(Forbid());
    }

    private bool CanManage(Guid restaurantId)
    {
        return User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage);
    }

    private async Task<IActionResult> Execute(Func<Task<CashRegisterResult>> operation)
    {
        CashRegisterResult result = await operation();
        return result.Outcome switch
        {
            CashRegisterOutcome.Ok => Ok(result.Value),
            CashRegisterOutcome.Created => Created(result.Location!, result.Value),
            CashRegisterOutcome.NoContent => NoContent(),
            CashRegisterOutcome.NotFound => NotFound(),
            CashRegisterOutcome.Conflict => Conflict(result.Value),
            CashRegisterOutcome.Validation => ValidationProblem(
                new ValidationProblemDetails(result.Errors!.ToDictionary())
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
