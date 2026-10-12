namespace Restaurantes.Dining.Application;

public sealed partial class DiningService(
    IDiningStore db,
    IDiningPayments payments,
    IDiningCashRegister cashRegister,
    IDiningAuthorization access,
    TimeProvider time
) { }
