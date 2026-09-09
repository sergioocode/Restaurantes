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
}
