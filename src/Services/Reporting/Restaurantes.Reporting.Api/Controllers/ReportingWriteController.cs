using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Reporting.Api.DeadLetters;

namespace Restaurantes.Reporting.Api.Controllers;

[Authorize(Roles = "Admin"), ApiController, Route("api/reporting/dead-letters")]
public sealed class ReportingWriteController(RabbitMqDeadLetterService deadLetters) : ControllerBase
{
    [HttpPost("{queue}/replay")]
    public async Task<ActionResult<DeadLetterReplayResult>> Replay(
        string queue,
        [FromQuery] int count,
        CancellationToken cancellationToken
    )
    {
        if (!deadLetters.IsKnownQueue(queue))
        {
            return NotFound();
        }

        if (count is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(count), "Count must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        return Ok(await deadLetters.ReplayAsync(queue, count, cancellationToken));
    }
}
