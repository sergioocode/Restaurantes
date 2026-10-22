using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.CashRegister.Api.Controllers;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Contracts.Requests;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterWriteControllerResultTests
{
    private static readonly MethodInfo ExecuteMethod =
        typeof(CashRegisterWriteController).GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

    [Fact]
    public async Task Execute_maps_ok_result_to_200_with_value()
    {
        object value = new { message = "ok" };

        IActionResult result = await Execute(new(CashRegisterOutcome.Ok, value));

        OkObjectResult response = Assert.IsType<OkObjectResult>(result);
        Assert.Same(value, response.Value);
    }

    [Fact]
    public async Task Execute_maps_created_result_to_201_with_location_and_value()
    {
        object value = new { id = Guid.NewGuid() };

        IActionResult result = await Execute(
            new(CashRegisterOutcome.Created, value, "/sessions/123")
        );

        CreatedResult response = Assert.IsType<CreatedResult>(result);
        Assert.Equal("/sessions/123", response.Location);
        Assert.Same(value, response.Value);
    }

    [Fact]
    public async Task Execute_maps_no_content_result_to_204()
    {
        IActionResult result = await Execute(new(CashRegisterOutcome.NoContent));

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Execute_maps_not_found_result_to_404()
    {
        IActionResult result = await Execute(new(CashRegisterOutcome.NotFound));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Execute_maps_conflict_result_to_409_with_value()
    {
        object value = new { detail = "conflict" };

        IActionResult result = await Execute(new(CashRegisterOutcome.Conflict, value));

        ConflictObjectResult response = Assert.IsType<ConflictObjectResult>(result);
        Assert.Same(value, response.Value);
    }

    [Fact]
    public async Task Execute_maps_validation_result_to_400_with_errors()
    {
        Dictionary<string, string[]> errors = new() { ["amount"] = ["invalid"] };

        IActionResult result = await Execute(new(CashRegisterOutcome.Validation, Errors: errors));

        BadRequestObjectResult response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        ValidationProblemDetails details = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(["invalid"], details.Errors["amount"]);
    }

    [Fact]
    public async Task Execute_maps_service_unavailable_result_to_503_with_value()
    {
        object value = new { detail = "unavailable" };

        IActionResult result = await Execute(new(CashRegisterOutcome.ServiceUnavailable, value));

        ObjectResult response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Same(value, response.Value);
    }

    [Fact]
    public async Task Write_endpoints_forbid_users_without_cash_register_permission()
    {
        CashRegisterWriteController controller = new(null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();

        Assert.IsType<ForbidResult>(
            await controller.Open(
                restaurantId,
                new OpenCashRegisterRequest(10m),
                CancellationToken.None
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.Close(
                restaurantId,
                sessionId,
                new CloseCashRegisterRequest([]),
                CancellationToken.None
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.CreateMovement(
                restaurantId,
                sessionId,
                new CreateCashMovementRequest("Income", "Cash", 5m, "Float", "Opening"),
                CancellationToken.None
            )
        );
    }

    private static async Task<IActionResult> Execute(CashRegisterResult result)
    {
        CashRegisterWriteController controller = new(null!, null!);
        Func<Task<CashRegisterResult>> operation = () => Task.FromResult(result);
        Task<IActionResult> response =
            (Task<IActionResult>)ExecuteMethod.Invoke(controller, [operation])!;

        return await response;
    }
}
