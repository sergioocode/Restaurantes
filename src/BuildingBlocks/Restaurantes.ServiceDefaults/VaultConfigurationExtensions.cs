using System.Net;
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
