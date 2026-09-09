using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Write;

internal static class DiningRequestContext
{
    public static DiningRequest FromHttpContext(HttpContext http) => new(
        http.User,
        http.Connection.RemoteIpAddress,
        http.Request.Headers["X-Customer-Session-Token"].FirstOrDefault(),
        http.Request.Headers.Authorization.FirstOrDefault()
    );

    public static IActionResult ToActionResult(this ControllerBase controller, DiningResult result)
    {
        if (result.GuestCount is int count)
        {
            controller.Response.Headers["X-Dining-Guest-Count"] = count.ToString(CultureInfo.InvariantCulture);
        }

        return result.Outcome switch
        {
            DiningOutcome.Ok => controller.Ok(result.Value),
            DiningOutcome.Created => controller.Created(result.Location, result.Value),
            DiningOutcome.NoContent => controller.NoContent(),
            DiningOutcome.BadRequest => controller.BadRequest(result.Value),
            DiningOutcome.Unauthorized => controller.Unauthorized(),
            DiningOutcome.Forbidden => controller.Forbid(),
            DiningOutcome.NotFound => controller.NotFound(result.Value),
            DiningOutcome.Conflict => controller.Conflict(result.Value),
            DiningOutcome.Problem => controller.Problem(statusCode: result.ProblemStatusCode, title: result.ProblemTitle, detail: result.ProblemDetail),
            _ => throw new InvalidOperationException($"Unsupported Dining outcome '{result.Outcome}'."),
        };
    }
}
