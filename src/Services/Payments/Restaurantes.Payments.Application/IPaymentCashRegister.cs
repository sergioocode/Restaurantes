namespace Restaurantes.Payments.Application;

public interface IPaymentCashRegister
{
    Task<CashRegisterSessionDetails> GetOpenSessionAsync(
        Guid restaurantId,
        CancellationToken cancellationToken
    );
}

public sealed record CashRegisterSessionDetails(Guid SessionId, DateOnly BusinessDate);
