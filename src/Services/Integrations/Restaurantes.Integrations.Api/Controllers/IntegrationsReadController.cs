using Microsoft.AspNetCore.Mvc;

namespace Restaurantes.Integrations.Api.Controllers;

[ApiController, Route("api/integrations")]
public sealed class IntegrationsReadController : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        return Ok(
            new Restaurantes.Integrations.Contracts.Responses.IntegrationsResponse(
                "Integrations",
                ["providers", "webhooks", "delivery"]
            )
        );
    }
}
