namespace Restaurantes.Dining.Application;

public sealed partial class DiningService(
    IDiningStore db,
    IDiningPayments payments,
    IDiningCashRegister cashRegister,
    IDiningNotifications realtime,
    IDiningAuthorization access,
    TimeProvider time
)
{
}
