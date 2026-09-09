using System.Security.Cryptography;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    private static Task PublishTableChanged(
        IDiningNotifications realtime,
        DiningSession session,
        string status,
        TimeProvider time
    )
    {
        return realtime.TableChanged(
            new DiningTableChanged(
                session.RestaurantId,
                session.TableId,
                status,
                status == "Occupied" ? session.Id : null,
                session.Source,
                time.GetUtcNow().UtcDateTime
            )
        );
    }

    private static object TableResponse(RestaurantTable table, DiningSession? session)
    {
        return new
        {
            table.Id,
            table.RestaurantId,
            table.Code,
            table.Label,
            table.ZoneId,
            table.RequestGuestCount,
            zoneName = table.Zone.Name,
            zoneSortOrder = table.Zone.SortOrder,
            table.QrCode,
            qrPath = $"/api/dining/qr/{table.QrCode}/sessions",
            customerQrPath = $"/qr/{table.QrCode}",
            table.IsActive,
            status = session is null ? "Available" : "Occupied",
            activeSessionId = session?.Id,
        };
    }

    private static object QrResponse(RestaurantTable table)
    {
        return new
        {
            table.Id,
            table.RestaurantId,
            table.Code,
            table.Label,
            table.ZoneId,
            table.RequestGuestCount,
            table.QrCode,
            qrPath = $"/api/dining/qr/{table.QrCode}/sessions",
            customerQrPath = $"/qr/{table.QrCode}",
        };
    }

    private static object QrSessionResponse(
        RestaurantTable table,
        DiningSession session,
        DiningRestaurantPolicy? policy
    )
    {
        return new
        {
            table = new
            {
                table.Id,
                table.RestaurantId,
                table.Code,
                table.Label,
                table.ZoneId,
                table.RequestGuestCount,
            },
            session = SessionResponse(session),
            customerAccessToken = session.CustomerAccessToken,
            qrRequiresImmediatePayment = policy?.QrRequiresImmediatePayment ?? true,
        };
    }

    private static string NewQrCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    private static string NewCustomerAccessToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static bool TokenEquals(string expected, string? supplied)
    {
        if (expected.Length == 0 || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied.Trim());
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static object SessionResponse(DiningSession session)
    {
        return new
        {
            session.Id,
            session.RestaurantId,
            session.TableId,
            session.RequestGuestCount,
            session.GuestCount,
            session.Source,
            session.Status,
            session.Version,
            session.OpenedAtUtc,
            session.ClosedAtUtc,
            session.PaidAtUtc,
            session.PaymentMethod,
            session.CancelledAtUtc,
            session.CancelledByUserId,
            session.CancellationReason,
            orders = session
                .Orders.OrderBy(x => x.AddedAtUtc)
                .Select(x => new
                {
                    x.OrderId,
                    x.OrderStatus,
                    x.PaymentStatus,
                    x.Amount,
                }),
        };
    }
}
