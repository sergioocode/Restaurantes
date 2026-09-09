namespace Restaurantes.CashRegister.Infrastructure.Persistence;

public sealed class CashRegisterInboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
