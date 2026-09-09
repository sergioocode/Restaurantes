namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record QrSessionEnvelope(
    QrTableResponse Table,
    QrDiningSessionResponse? Session,
    string CustomerAccessToken,
    bool QrRequiresImmediatePayment
);
