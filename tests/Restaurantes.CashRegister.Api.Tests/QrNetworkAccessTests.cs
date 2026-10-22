using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class QrNetworkAccessTests
{
    [Theory]
    [InlineData("192.168.1.27", "192.168.1.0/24", true)]
    [InlineData("192.168.2.27", "192.168.1.0/24", false)]
    [InlineData("192.168.1.27", "192.168.1.27", true)]
    [InlineData("192.168.1.28", "192.168.1.27", false)]
    [InlineData("10.20.30.40", "10.0.0.0/8", true)]
    [InlineData("10.20.30.40", "invalid/8", false)]
    [InlineData("10.20.30.40", "10.0.0.0/33", false)]
    [InlineData("10.20.30.40", "10.0.0.0/-1", false)]
    [InlineData("2001:db8:1::25", "2001:db8:1::/48", true)]
    [InlineData("2001:db8:2::25", "2001:db8:1::/48", false)]
    [InlineData("192.168.1.27", "2001:db8::/32", false)]
    public void Network_ranges_allow_only_clients_inside_the_configured_cidr(
        string address,
        string network,
        bool expected
    )
    {
        Assert.Equal(expected, QrNetworkAccess.IsAllowed(IPAddress.Parse(address), [network]));
    }

    [Fact]
    public void Missing_address_and_invalid_networks_are_denied()
    {
        Assert.False(QrNetworkAccess.IsAllowed(null, ["192.168.1.0/24"]));
        Assert.False(
            QrNetworkAccess.IsAllowed(
                IPAddress.Parse("192.168.1.27"),
                ["not-an-address", "192.168.1.0/abc"]
            )
        );
    }

    [Fact]
    public void Mapped_ipv4_addresses_match_ipv4_networks()
    {
        IPAddress mappedAddress = IPAddress.Parse("::ffff:192.168.1.27");
        IPAddress mappedNetwork = IPAddress.Parse("::ffff:192.168.1.0");

        Assert.True(QrNetworkAccess.IsAllowed(mappedAddress, ["192.168.1.0/24"]));
        Assert.True(
            QrNetworkAccess.IsAllowed(IPAddress.Parse("192.168.1.27"), [$"{mappedNetwork}/24"])
        );
    }

    [Fact]
    public void Denied_result_uses_configured_client_networks_and_returns_forbidden_details()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["QrAccess:AllowedClientNetworks:0"] = "192.168.1.0/24",
                }
            )
            .Build();
        DefaultHttpContext allowed = new();
        allowed.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.27");
        DefaultHttpContext denied = new();
        denied.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        Assert.Null(QrNetworkAccess.DeniedResult(allowed, configuration));
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(
            QrNetworkAccess.DeniedResult(denied, configuration)
        );
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var details = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(result.Value);
        Assert.Equal("Acceso QR no autorizado", details.Title);
        Assert.Equal(StatusCodes.Status403Forbidden, details.Status);
    }
}
