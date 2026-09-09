namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record PaymentMethodTotal(string Method, decimal Sales, decimal Refunds, decimal Net);
