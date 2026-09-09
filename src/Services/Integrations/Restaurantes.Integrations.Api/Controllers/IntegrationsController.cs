using Microsoft.AspNetCore.Mvc;

namespace Restaurantes.Integrations.Api.Controllers;

[ApiController, Route("api/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        return Ok(
            new
            {
                service = "Integrations",
                capabilities = new[] { "providers", "webhooks", "delivery" },
            }
        );
    }
}
