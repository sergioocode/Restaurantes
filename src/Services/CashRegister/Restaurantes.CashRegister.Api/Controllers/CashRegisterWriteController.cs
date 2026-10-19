using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.Security;

namespace Restaurantes.CashRegister.Api.Controllers;

[ApiController, Authorize, Route("api/cash-register/restaurants/{restaurantId:guid}")]
public sealed class CashRegisterWriteController(
    CashRegisterService service,
    CashRegisterDbContext db
) : ControllerBase
{
    [HttpPost("open")]
    public Task<IActionResult> Open(
        Guid restaurantId,
        OpenCashRegisterRequest request,
        CancellationToken ct
    )
    {
        return CanManage(restaurantId)
            ? ExecuteTransactional(() => service.Open(restaurantId, request, CurrentUser(), ct), ct)
            : Task.FromResult<IActionResult>(Forbid());
    }

    [HttpPost("sessions/{sessionId:guid}/close")]
    public Task<IActionResult> Close(
        Guid restaurantId,
        Guid sessionId,
        CloseCashRegisterRequest request,
        CancellationToken ct
    )
    {
        return CanManage(restaurantId)
            ? ExecuteTransactional(
                () =>
                    service.Close(
                        restaurantId,
                        sessionId,
                        request,
                        CurrentUser(),
                        Request.Headers.Authorization.ToString(),
                        ct
                    ),
                ct
            )
            : Task.FromResult<IActionResult>(Forbid());
    }

    [HttpPost("sessions/{sessionId:guid}/movements")]
    public Task<IActionResult> CreateMovement(
        Guid restaurantId,
        Guid sessionId,
        CreateCashMovementRequest request,
        CancellationToken ct
    )
    {
        return CanManage(restaurantId)
            ? ExecuteTransactional(
                () => service.CreateMovement(restaurantId, sessionId, request, CurrentUser(), ct),
                ct
            )
            : Task.FromResult<IActionResult>(Forbid());
    }

    private async Task<IActionResult> ExecuteTransactional(
        Func<Task<CashRegisterResult>> operation,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        IActionResult result = await Execute(operation);
        await transaction.CommitAsync(ct);
        return result;
    }

    private bool CanManage(Guid restaurantId)
    {
        return User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage);
    }

    private CashRegisterUser CurrentUser()
    {
        return new(
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id)
                ? id
                : throw new InvalidOperationException("El token no contiene usuario."),
            User.Identity?.Name ?? "Usuario"
        );
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
            CashRegisterOutcome.ServiceUnavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                result.Value
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
