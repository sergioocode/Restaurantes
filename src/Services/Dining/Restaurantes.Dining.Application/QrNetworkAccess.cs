using System.Net;
using System.Net.Sockets;

namespace Restaurantes.Dining.Application;

internal static class QrNetworkAccess
{
    private const int MaximumNetworks = 20;

    public static bool TryNormalize(
        IEnumerable<string>? values,
        out string normalized,
        out string? error
    )
    {
        string[] candidates = (values ?? [])
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length > MaximumNetworks)
        {
            normalized = string.Empty;
            error = $"A maximum of {MaximumNetworks} QR networks is allowed.";
            return false;
        }

        List<string> result = [];
        foreach (string candidate in candidates)
        {
            if (!TryParse(candidate, out IPAddress? address, out int prefixLength))
            {
                normalized = string.Empty;
                error = $"'{candidate}' is not a valid IP address or CIDR network.";
                return false;
            }

            result.Add($"{address}/{prefixLength}");
        }

        normalized = string.Join(';', result);
        error = null;
        return true;
    }

    public static bool IsAllowed(IPAddress? clientAddress, string configuredNetworks)
    {
        if (clientAddress is null)
        {
            return false;
        }

        IPAddress normalizedClient = NormalizeAddress(clientAddress);
        foreach (
            string configured in configuredNetworks.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (
                TryParse(configured, out IPAddress? network, out int prefixLength)
                && Contains(network, prefixLength, normalizedClient)
            )
            {
                return true;
            }
        }

        return false;
    }

    public static string[] ToResponse(string configuredNetworks)
    {
        return configuredNetworks.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
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
