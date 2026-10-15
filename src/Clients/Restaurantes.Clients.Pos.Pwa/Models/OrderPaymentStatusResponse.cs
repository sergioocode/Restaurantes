namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderPaymentStatusResponse(Guid OrderId, string Status);
