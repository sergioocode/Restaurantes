using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Restaurantes.ServiceDefaults;

public static class VaultConfigurationExtensions
{
    private const string DefaultLocalAddress = "http://127.0.0.1:8201";
    private const string DefaultSecretPath = "secret/data/restaurantes/local";

    public static IHostApplicationBuilder AddVaultConfiguration(
        this IHostApplicationBuilder builder
    )
    {
        string? address = builder.Configuration["Vault:Address"];
        if (string.IsNullOrWhiteSpace(address))
        {
            if (!builder.Environment.IsDevelopment())
            {
                return builder;
            }

            address = DefaultLocalAddress;
            StartLocalVault(builder.Environment.ContentRootPath);
        }

        string secretPath = builder.Configuration["Vault:SecretPath"] ?? DefaultSecretPath;
        IReadOnlyDictionary<string, string?> secrets = ReadSecrets(address, secretPath);
        builder.Configuration.AddInMemoryCollection(secrets);
        return builder;
    }

    public static string GetDesignTimeConnectionString(string name)
    {
        IConfigurationRoot environmentConfiguration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        string? address = environmentConfiguration["Vault:Address"];
        if (string.IsNullOrWhiteSpace(address))
        {
            address = DefaultLocalAddress;
            StartLocalVault(Directory.GetCurrentDirectory());
        }

        string secretPath = environmentConfiguration["Vault:SecretPath"] ?? DefaultSecretPath;
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(environmentConfiguration)
            .AddInMemoryCollection(ReadSecrets(address, secretPath))
            .Build();

        return configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException(
                $"Vault does not define the connection string '{name}' for design-time EF operations."
            );
    }

    private static void StartLocalVault(string contentRootPath)
    {
        if (IsLocalVaultProxyAvailable())
        {
            return;
        }

        string composeDirectory = FindComposeDirectory(contentRootPath);
        using Mutex startupLock = new(false, "Restaurantes.Vault.Startup");
        bool lockAcquired;
        try
        {
            lockAcquired = startupLock.WaitOne(TimeSpan.FromMinutes(3));
        }
        catch (AbandonedMutexException)
        {
            lockAcquired = true;
        }

        if (!lockAcquired)
        {
            throw new InvalidOperationException(
                "Timed out waiting for another Restaurantes process to start local Vault."
            );
        }

        try
        {
            if (IsLocalVaultProxyAvailable())
            {
                return;
            }

            string rootToken = EnsureLocalRootToken(composeDirectory);
            ProcessStartInfo startInfo = new("docker")
            {
                WorkingDirectory = composeDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("compose");
            startInfo.ArgumentList.Add("up");
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add("vault-proxy");
            startInfo.Environment["VAULT_DEV_ROOT_TOKEN_ID"] = rootToken;

            using Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker Compose.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException(
                    "Timed out starting local Vault with Docker Compose."
                );
            }

            Task.WaitAll(standardOutput, standardError);
            if (process.ExitCode != 0)
            {
                string details = string.Join(
                        Environment.NewLine,
                        standardOutput.Result,
                        standardError.Result
                    )
                    .Trim();
                throw new InvalidOperationException(
                    $"Could not start local Vault with Docker Compose. {details}"
                );
            }

            WaitForLocalVaultProxy();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not start local Vault automatically. Make sure Docker Desktop is running and available on PATH.",
                ex
            );
        }
        finally
        {
            startupLock.ReleaseMutex();
        }
    }

    private static bool IsLocalVaultProxyAvailable()
    {
        using TcpClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(300));
        try
        {
            client.ConnectAsync(IPAddress.Loopback, 8201, timeout.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static void WaitForLocalVaultProxy()
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (IsLocalVaultProxyAvailable())
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }

        throw new InvalidOperationException(
            "Docker Compose started the local Vault containers, but the Vault Proxy did not start listening on port 8201."
        );
    }

    private static string EnsureLocalRootToken(string composeDirectory)
    {
        string envFilePath = Path.Combine(composeDirectory, "tools", "vault", ".env");
        const string key = "VAULT_DEV_ROOT_TOKEN_ID";
        if (File.Exists(envFilePath))
        {
            string? token = File.ReadLines(envFilePath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith($"{key}=", StringComparison.Ordinal))
                .Select(line => line[(key.Length + 1)..].Trim().Trim('"', '\''))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return token is not null
                ? token
                : throw new InvalidOperationException(
                    $"'{envFilePath}' exists but does not define {key}."
                );
        }

        string newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(
            envFilePath,
            $"{key}={newToken}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(envFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return newToken;
    }

    private static string FindComposeDirectory(string contentRootPath)
    {
        foreach (string startPath in new[] { contentRootPath, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? directory = new(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not find docker-compose.yml to start local Vault."
        );
    }

    private static IReadOnlyDictionary<string, string?> ReadSecrets(
        string address,
        string secretPath
    )
    {
        Uri requestUri = new($"{address.TrimEnd('/')}/v1/{secretPath.TrimStart('/')}");
        Exception? lastError = null;

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                using HttpResponseMessage response = client
                    .GetAsync(requestUri)
                    .GetAwaiter()
                    .GetResult();
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException(
                        $"Vault secret '{secretPath}' does not exist."
                    );
                }

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        $"Vault denied access to secret '{secretPath}' (HTTP {(int)response.StatusCode}). Check the local AppRole policy."
                    );
                }

                response.EnsureSuccessStatusCode();
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return ParseSecrets(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                if (attempt < 10)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(500));
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not load configuration from Vault at '{address}'. Check that the local Vault containers are healthy.",
            lastError
        );
    }

    private static IReadOnlyDictionary<string, string?> ParseSecrets(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data").GetProperty("data");
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in data.EnumerateObject())
        {
            string key = property.Name.Replace("__", ConfigurationPath.KeyDelimiter);
            values[key] =
                property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
        }

        return values;
    }
}
