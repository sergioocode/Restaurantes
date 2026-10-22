using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Restaurantes.ServiceDefaults;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class VaultConfigurationExtensionsTests
{
    private const string SecretPath = "secret/data/test";

    [Fact]
    public void Production_host_without_vault_address_keeps_its_configuration()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration["Vault:Address"] = "";
        builder.Configuration["Existing:Value"] = "preserved";
        string? signingKeyBefore = builder.Configuration["Security:SigningKey"];

        IHostApplicationBuilder returned = builder.AddVaultConfiguration();

        Assert.Same(builder, returned);
        Assert.Equal("preserved", builder.Configuration["Existing:Value"]);
        Assert.Equal(signingKeyBefore, builder.Configuration["Security:SigningKey"]);
    }

    [Fact]
    public async Task Development_host_loads_and_flattens_vault_secret_values()
    {
        const string body =
            "{\"data\":{\"data\":{\"Security__SigningKey\":\"test-secret\",\"Feature__Enabled\":true,\"Server__Port\":8123}}}";
        using TcpListener listener = StartServer(out Uri address);
        Task server = RespondOnceAsync(listener, HttpStatusCode.OK, body);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.Configuration["Vault:Address"] = address.ToString();
        builder.Configuration["Vault:SecretPath"] = SecretPath;

        builder.AddVaultConfiguration();
        await server;

        Assert.Equal("test-secret", builder.Configuration["Security:SigningKey"]);
        Assert.Equal("True", builder.Configuration["Feature:Enabled"]);
        Assert.Equal("8123", builder.Configuration["Server:Port"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "does not exist")]
    [InlineData(HttpStatusCode.Forbidden, "denied access")]
    [InlineData(HttpStatusCode.Unauthorized, "denied access")]
    public async Task Vault_missing_secret_or_denied_access_reports_the_cause(
        HttpStatusCode statusCode,
        string expectedMessage
    )
    {
        using TcpListener listener = StartServer(out Uri address);
        Task server = RespondOnceAsync(listener, statusCode, "{}");
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Vault:Address"] = address.ToString();
        builder.Configuration["Vault:SecretPath"] = SecretPath;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddVaultConfiguration()
        );
        await server;

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Design_time_connection_string_uses_vault_environment_settings()
    {
        using TcpListener listener = StartServer(out Uri address);
        Task server = RespondOnceAsync(
            listener,
            HttpStatusCode.OK,
            "{\"data\":{\"data\":{\"ConnectionStrings__OrdersWrite\":\"Host=db;Database=orders;\"}}}"
        );
        Dictionary<string, string?> previous = new(StringComparer.Ordinal)
        {
            ["Vault__Address"] = Environment.GetEnvironmentVariable("Vault__Address"),
            ["Vault__SecretPath"] = Environment.GetEnvironmentVariable("Vault__SecretPath"),
        };

        try
        {
            Environment.SetEnvironmentVariable("Vault__Address", address.ToString());
            Environment.SetEnvironmentVariable("Vault__SecretPath", SecretPath);

            string connectionString = VaultConfigurationExtensions.GetDesignTimeConnectionString(
                "OrdersWrite"
            );
            await server;

            Assert.Equal("Host=db;Database=orders;", connectionString);
        }
        finally
        {
            foreach ((string name, string? value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static TcpListener StartServer(out Uri address)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        address = new Uri($"http://127.0.0.1:{port}");
        return listener;
    }

    private static async Task RespondOnceAsync(
        TcpListener listener,
        HttpStatusCode statusCode,
        string body
    )
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, leaveOpen: true);
        _ = await reader.ReadLineAsync();
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string responseHeader =
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeader));
        await stream.WriteAsync(bodyBytes);
    }
}
