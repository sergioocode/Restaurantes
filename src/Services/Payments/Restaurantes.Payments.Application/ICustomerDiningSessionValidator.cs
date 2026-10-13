namespace Restaurantes.Payments.Application;

public interface ICustomerDiningSessionValidator
{
    Task<bool> IsValidAsync(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string serviceMode,
        string customerAccessToken,
        CancellationToken cancellationToken
    );

    Task<bool> IsChargeAmountValidAsync(
        Guid sessionId,
        Guid orderId,
        Guid restaurantId,
        decimal amount,
        string? authorization,
        CancellationToken cancellationToken
    );
}
