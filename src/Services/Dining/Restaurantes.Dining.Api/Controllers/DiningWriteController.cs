using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Controllers;

[ApiController, Authorize, Route("api/dining")]
public sealed class DiningWriteController(DiningService service) : ControllerBase
{
    [HttpPost("restaurants/{restaurantId:guid}/tables")]
    public Task<IActionResult> CreateTable(
        Guid restaurantId,
        CreateTableRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.CreateTable(restaurantId, request, User, ct));
    }

    [HttpPut("tables/{tableId:guid}")]
    public Task<IActionResult> UpdateTable(
        Guid tableId,
        UpdateTableRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.UpdateTable(tableId, request, User, ct));
    }

    [HttpDelete("tables/{tableId:guid}")]
    public Task<IActionResult> DeleteTable(Guid tableId, CancellationToken ct)
    {
        return Execute(() => service.DeleteTable(tableId, User, ct));
    }

    [HttpPut("restaurants/{restaurantId:guid}/policy")]
    public Task<IActionResult> UpdatePolicy(
        Guid restaurantId,
        UpdateDiningPolicyRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.UpdatePolicy(restaurantId, request, User, ct));
    }

    [HttpPost("tables/{tableId:guid}/sessions")]
    public Task<IActionResult> OpenSession(
        Guid tableId,
        OpenSessionRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.OpenSession(tableId, request, User, ct));
    }

    [HttpPost("tables/{tableId:guid}/qr/rotate")]
    public Task<IActionResult> RotateQr(Guid tableId, CancellationToken ct)
    {
        return Execute(() => service.RotateQr(tableId, User, ct));
    }

    [AllowAnonymous]
    [HttpPost("qr/{qrCode}/sessions")]
    public Task<IActionResult> OpenQrSession(string qrCode, int? guestCount, CancellationToken ct)
    {
        return Execute(() =>
            service.OpenQrSession(
                qrCode,
                guestCount,
                DiningRequestContext.FromHttpContext(HttpContext),
                ct
            )
        );
    }

    [AllowAnonymous]
    [HttpPut("sessions/{sessionId:guid}/guests")]
    public Task<IActionResult> SetGuestCount(
        Guid sessionId,
        SetGuestCountRequest request,
        CancellationToken ct
    )
    {
        return Execute(() =>
            service.SetGuestCount(
                sessionId,
                request,
                DiningRequestContext.FromHttpContext(HttpContext),
                ct
            )
        );
    }

    [HttpPost("sessions/{sessionId:guid}/checkout")]
    public Task<IActionResult> Checkout(
        Guid sessionId,
        CheckoutSessionRequest request,
        CancellationToken ct
    )
    {
        return Execute(() =>
            service.Checkout(
                sessionId,
                request,
                User,
                DiningRequestContext.FromHttpContext(HttpContext),
                ct
            )
        );
    }

    [HttpPost("sessions/{sessionId:guid}/close")]
    public Task<IActionResult> CloseSession(Guid sessionId, CancellationToken ct)
    {
        return Execute(() => service.CloseSession(sessionId, User, ct));
    }

    [HttpPost("sessions/{sessionId:guid}/cancel")]
    public Task<IActionResult> CancelSession(Guid sessionId, CancellationToken ct)
    {
        return Execute(() => service.CancelSession(sessionId, User, ct));
    }

    [HttpPost("restaurants/{restaurantId:guid}/zones")]
    public Task<IActionResult> CreateZone(
        Guid restaurantId,
        SaveZoneRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.CreateZone(restaurantId, request, User, ct));
    }

    [HttpPut("restaurants/{restaurantId:guid}/zones/{zoneId:guid}")]
    public Task<IActionResult> UpdateZone(
        Guid restaurantId,
        Guid zoneId,
        SaveZoneRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.UpdateZone(restaurantId, zoneId, request, User, ct));
    }

    [HttpDelete("restaurants/{restaurantId:guid}/zones/{zoneId:guid}")]
    public Task<IActionResult> DeleteZone(Guid restaurantId, Guid zoneId, CancellationToken ct)
    {
        return Execute(() => service.DeleteZone(restaurantId, zoneId, User, ct));
    }

    private async Task<IActionResult> Execute(Func<Task<DiningResult>> operation)
    {
        return this.ToActionResult(await operation());
    }
}
