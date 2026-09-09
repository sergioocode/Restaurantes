using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Api.Write;

namespace Restaurantes.Dining.Api.Write.Controllers;

[ApiController, Authorize, Route("api/dining")]
public sealed class DiningController(DiningService service) : ControllerBase
{
    [HttpPost("restaurants/{restaurantId:guid}/tables")]
    public Task<IActionResult> CreateTable(Guid restaurantId, CreateTableRequest request, CancellationToken ct) =>
        Execute(() => service.CreateTable(restaurantId, request, User, ct));

    [HttpGet("restaurants/{restaurantId:guid}/tables")]
    public Task<IActionResult> ListTables(Guid restaurantId, CancellationToken ct) =>
        Execute(() => service.ListTables(restaurantId, User, ct));

    [HttpPut("tables/{tableId:guid}")]
    public Task<IActionResult> UpdateTable(Guid tableId, UpdateTableRequest request, CancellationToken ct) =>
        Execute(() => service.UpdateTable(tableId, request, User, ct));

    [HttpDelete("tables/{tableId:guid}")]
    public Task<IActionResult> DeleteTable(Guid tableId, CancellationToken ct) =>
        Execute(() => service.DeleteTable(tableId, User, ct));

    [HttpGet("restaurants/{restaurantId:guid}/policy")]
    public Task<IActionResult> GetPolicy(Guid restaurantId, CancellationToken ct) =>
        Execute(() => service.GetPolicy(restaurantId, User, ct));

    [HttpPut("restaurants/{restaurantId:guid}/policy")]
    public Task<IActionResult> UpdatePolicy(
        Guid restaurantId,
        UpdateDiningPolicyRequest request,
        CancellationToken ct
    ) => Execute(() => service.UpdatePolicy(restaurantId, request, User, ct));

    [HttpPost("tables/{tableId:guid}/sessions")]
    public Task<IActionResult> OpenSession(Guid tableId, OpenSessionRequest request, CancellationToken ct) =>
        Execute(() => service.OpenSession(tableId, request, User, ct));

    [HttpPost("tables/{tableId:guid}/qr/rotate")]
    public Task<IActionResult> RotateQr(Guid tableId, CancellationToken ct) =>
        Execute(() => service.RotateQr(tableId, User, ct));

    [AllowAnonymous]
    [HttpGet("qr/{qrCode}")]
    public Task<IActionResult> PreviewQr(string qrCode, CancellationToken ct) =>
        Execute(() => service.PreviewQr(qrCode, DiningRequestContext.FromHttpContext(HttpContext), ct));

    [AllowAnonymous]
    [HttpPost("qr/{qrCode}/sessions")]
    public Task<IActionResult> OpenQrSession(string qrCode, int? guestCount, CancellationToken ct) =>
        Execute(() => service.OpenQrSession(qrCode, guestCount, DiningRequestContext.FromHttpContext(HttpContext), ct));

    [HttpGet("tables/{tableId:guid}/active-session")]
    public Task<IActionResult> ActiveSession(Guid tableId, CancellationToken ct) =>
        Execute(() => service.ActiveSession(tableId, User, ct));

    [HttpGet("sessions/{sessionId:guid}")]
    public Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct) =>
        Execute(() => service.GetSession(sessionId, User, ct));

    [AllowAnonymous]
    [HttpPut("sessions/{sessionId:guid}/guests")]
    public Task<IActionResult> SetGuestCount(
        Guid sessionId,
        SetGuestCountRequest request,
        CancellationToken ct
    ) => Execute(() => service.SetGuestCount(sessionId, request, DiningRequestContext.FromHttpContext(HttpContext), ct));

    [HttpGet("sessions/{sessionId:guid}/bill")]
    public Task<IActionResult> GetBill(Guid sessionId, CancellationToken ct) =>
        Execute(() => service.GetBill(sessionId, User, ct));

    [HttpPost("sessions/{sessionId:guid}/checkout")]
    public Task<IActionResult> Checkout(
        Guid sessionId,
        CheckoutSessionRequest request,
        CancellationToken ct
    ) => Execute(() => service.Checkout(sessionId, request, User, DiningRequestContext.FromHttpContext(HttpContext), ct));

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/validate")]
    public Task<IActionResult> ValidateSession(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string? source,
        string? serviceMode,
        CancellationToken ct
    ) => Execute(() => service.ValidateSession(sessionId, restaurantId, tableId, source, serviceMode, DiningRequestContext.FromHttpContext(HttpContext), ct));

    [HttpPost("sessions/{sessionId:guid}/close")]
    public Task<IActionResult> CloseSession(Guid sessionId, CancellationToken ct) =>
        Execute(() => service.CloseSession(sessionId, User, ct));

    [HttpPost("sessions/{sessionId:guid}/cancel")]
    public Task<IActionResult> CancelSession(
        Guid sessionId,
        CancelSessionRequest request,
        CancellationToken ct
    ) => Execute(() => service.CancelSession(sessionId, request, User, ct));

    [HttpGet("restaurants/{restaurantId:guid}/zones")]
    public Task<IActionResult> ListZones(Guid restaurantId, CancellationToken ct) =>
        Execute(() => service.ListZones(restaurantId, User, ct));

    [HttpPost("restaurants/{restaurantId:guid}/zones")]
    public Task<IActionResult> CreateZone(Guid restaurantId, SaveZoneRequest request, CancellationToken ct) =>
        Execute(() => service.CreateZone(restaurantId, request, User, ct));

    [HttpPut("restaurants/{restaurantId:guid}/zones/{zoneId:guid}")]
    public Task<IActionResult> UpdateZone(
        Guid restaurantId,
        Guid zoneId,
        SaveZoneRequest request,
        CancellationToken ct
    ) => Execute(() => service.UpdateZone(restaurantId, zoneId, request, User, ct));

    [HttpDelete("restaurants/{restaurantId:guid}/zones/{zoneId:guid}")]
    public Task<IActionResult> DeleteZone(Guid restaurantId, Guid zoneId, CancellationToken ct) =>
        Execute(() => service.DeleteZone(restaurantId, zoneId, User, ct));

    private async Task<IActionResult> Execute(Func<Task<DiningResult>> operation) =>
        this.ToActionResult(await operation());
}
