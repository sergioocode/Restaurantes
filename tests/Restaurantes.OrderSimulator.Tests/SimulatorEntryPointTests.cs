using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorEntryPointTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Main_stops_before_simulation_when_menu_has_no_available_products()
    {
        Guid restaurantId = Guid.NewGuid();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<string> requestTask = ReturnEmptyMenuAsync(listener, timeout.Token);
        string configPath = Path.Combine(Path.GetTempPath(), $"simulator-{Guid.NewGuid():N}.json");
        string configuration = JsonSerializer.Serialize(
            new
            {
                ApiBaseUrl = $"http://127.0.0.1:{port}",
                AccessToken = "preflight-token",
                Restaurants = new[]
                {
                    new { RestaurantId = restaurantId, Name = "Local de prueba" },
                },
            },
            WebJsonOptions
        );

        try
        {
            await File.WriteAllTextAsync(configPath, configuration, timeout.Token);

            int exitCode = await Simulator.Main(["--config", configPath]);
            string request = await requestTask;

            Assert.Equal(1, exitCode);
            Assert.StartsWith(
                $"GET /api/catalog/restaurants/{restaurantId}/menu HTTP/1.1",
                request
            );
            Assert.Contains("Authorization: Bearer preflight-token", request);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Main_returns_a_failure_for_invalid_arguments()
    {
        int exitCode = await Simulator.Main(["--unknown"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_reports_runtime_failure_after_all_preflight_checks_pass()
    {
        Guid restaurantId = Guid.NewGuid();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<int> requestCountTask = ReturnPreflightSuccessThenEmptyMenuAsync(
            listener,
            restaurantId,
            timeout.Token
        );
        string configPath = Path.Combine(Path.GetTempPath(), $"simulator-{Guid.NewGuid():N}.json");
        string configuration = JsonSerializer.Serialize(
            new
            {
                ApiBaseUrl = $"http://127.0.0.1:{port}",
                AccessToken = "preflight-token",
                KitchenStepDelaySeconds = 1,
                RunDurationMinutes = 1,
                Restaurants = new[]
                {
                    new { RestaurantId = restaurantId, Name = "Local de prueba" },
                },
            },
            WebJsonOptions
        );

        try
        {
            await File.WriteAllTextAsync(configPath, configuration, timeout.Token);

            int exitCode = await Simulator.Main(["--config", configPath]);
            int requestCount = await requestCountTask;

            Assert.Equal(1, exitCode);
            Assert.Equal(5, requestCount);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private static async Task<string> ReturnEmptyMenuAsync(
        TcpListener listener,
        CancellationToken cancellationToken
    )
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        using MemoryStream requestBytes = new();
        byte[] buffer = new byte[1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, read);
            if (requestBytes.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        string request = Encoding.ASCII.GetString(requestBytes.ToArray());
        byte[] response =
        [
            .. Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n"
            ),
            .. "[]"u8.ToArray(),
        ];
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return request;
    }

    private static async Task<int> ReturnPreflightSuccessThenEmptyMenuAsync(
        TcpListener listener,
        Guid restaurantId,
        CancellationToken cancellationToken
    )
    {
        int requestCount = 0;
        int menuRequestCount = 0;
        while (menuRequestCount < 2)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            NetworkStream stream = client.GetStream();
            string request = await ReadRequestAsync(stream, cancellationToken);
            string path = request.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            if (path == $"/api/cash-register/restaurants/{restaurantId}/current")
            {
                await stream.WriteAsync(
                    "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray(),
                    cancellationToken
                );
                requestCount++;
                continue;
            }

            object response;
            if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
            {
                menuRequestCount++;
                response =
                    menuRequestCount == 1
                        ? new[]
                        {
                            new MenuItemResponse(
                                restaurantId,
                                Guid.NewGuid(),
                                "TEST",
                                "Producto de prueba",
                                Guid.NewGuid(),
                                "FOOD",
                                "Comida",
                                10m,
                                true,
                                "CHEF",
                                "Cocina",
                                1,
                                DateTime.UnixEpoch
                            ),
                        }
                        : Array.Empty<MenuItemResponse>();
            }
            else
            {
                response = Array.Empty<object>();
            }

            byte[] body = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(response, WebJsonOptions)
            );
            byte[] headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"
            );
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            requestCount++;
        }

        return requestCount;
    }

    private static async Task<string> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        using MemoryStream requestBytes = new();
        byte[] buffer = new byte[1024];
        while (requestBytes.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) < 0)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, read);
        }

        return Encoding.ASCII.GetString(requestBytes.ToArray());
    }
}
