using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Controllers;

[ApiController, Authorize, Route("api/dining")]
public sealed class DiningReadController(DiningService service) : ControllerBase
{
    [HttpGet("restaurants/{restaurantId:guid}/tables")]
    public Task<IActionResult> ListTables(Guid restaurantId, CancellationToken ct)
    {
        return Execute(() => service.ListTables(restaurantId, User, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/policy")]
    public Task<IActionResult> GetPolicy(Guid restaurantId, CancellationToken ct)
    {
        return Execute(() => service.GetPolicy(restaurantId, User, ct));
    }

    [AllowAnonymous]
    [HttpGet("qr/{qrCode}")]
    public Task<IActionResult> PreviewQr(string qrCode, CancellationToken ct)
    {
        return Execute(() =>
            service.PreviewQr(qrCode, DiningRequestContext.FromHttpContext(HttpContext), ct)
        );
    }

    [HttpGet("tables/{tableId:guid}/active-session")]
    public Task<IActionResult> ActiveSession(Guid tableId, CancellationToken ct)
    {
        return Execute(() => service.ActiveSession(tableId, User, ct));
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct)
    {
        return Execute(() => service.GetSession(sessionId, User, ct));
    }

    [HttpGet("sessions/{sessionId:guid}/bill")]
    public Task<IActionResult> GetBill(Guid sessionId, CancellationToken ct)
    {
        return Execute(() => service.GetBill(sessionId, User, ct));
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/validate")]
    public Task<IActionResult> ValidateSession(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string? source,
        string? serviceMode,
        CancellationToken ct
    )
    {
        return Execute(() =>
            service.ValidateSession(
                sessionId,
                restaurantId,
                tableId,
                source,
                serviceMode,
                DiningRequestContext.FromHttpContext(HttpContext),
                ct
            )
        );
    }

    [HttpGet("restaurants/{restaurantId:guid}/zones")]
    public Task<IActionResult> ListZones(Guid restaurantId, CancellationToken ct)
    {
        return Execute(() => service.ListZones(restaurantId, User, ct));
    }

    private async Task<IActionResult> Execute(Func<Task<DiningResult>> operation)
    {
        return this.ToActionResult(await operation());
    }
}
