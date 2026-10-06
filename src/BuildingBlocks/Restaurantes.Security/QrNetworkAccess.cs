using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Restaurantes.Security;

public static class QrNetworkAccess
{
    public static ObjectResult? DeniedResult(HttpContext context, IConfiguration configuration)
    {
        string[] allowedNetworks =
            configuration.GetSection("QrAccess:AllowedClientNetworks").Get<string[]>() ?? [];
        return IsAllowed(context.Connection.RemoteIpAddress, allowedNetworks)
            ? null
            : new ObjectResult(
                new ProblemDetails
                {
                    Title = "Acceso QR no autorizado",
                    Detail = "La IP desde la que accedes no está configurada para usar QR.",
                    Status = StatusCodes.Status403Forbidden,
                }
            )
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
    }

    public static bool IsAllowed(IPAddress? clientAddress, IEnumerable<string> allowedNetworks)
    {
        if (clientAddress is null)
        {
            return false;
        }

        IPAddress normalizedClient = NormalizeAddress(clientAddress);
        foreach (string configuredNetwork in allowedNetworks)
        {
            if (
                TryParse(configuredNetwork, out IPAddress network, out int prefixLength)
                && Contains(network, prefixLength, normalizedClient)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParse(string value, out IPAddress address, out int prefixLength)
    {
        string[] parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out IPAddress? parsed))
        {
            address = IPAddress.None;
            prefixLength = 0;
            return false;
        }

        address = NormalizeAddress(parsed);
        int maximum = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (parts.Length == 1)
        {
            prefixLength = maximum;
            return true;
        }

        return int.TryParse(parts[1], out prefixLength)
            && prefixLength >= 0
            && prefixLength <= maximum;
    }

    private static bool Contains(IPAddress network, int prefixLength, IPAddress client)
    {
        network = NormalizeAddress(network);
        client = NormalizeAddress(client);
        if (network.AddressFamily != client.AddressFamily)
        {
            return false;
        }

        byte[] networkBytes = network.GetAddressBytes();
        byte[] clientBytes = client.GetAddressBytes();
        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int index = 0; index < fullBytes; index++)
        {
            if (networkBytes[index] != clientBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        int mask = 0xFF << (8 - remainingBits);
        return (networkBytes[fullBytes] & mask) == (clientBytes[fullBytes] & mask);
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
