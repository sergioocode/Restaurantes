using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Restaurantes.Identity.Api.Controllers;
using Xunit;

namespace Restaurantes.Identity.Application.Tests;

public sealed class IdentityApiStartupConfigurationTests
{
    private static readonly MethodInfo ConfiguredMethod = typeof(IdentityReadController)
        .Assembly.GetType("Program", throwOnError: true)!
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name.Contains("Configured", StringComparison.Ordinal));
    private static readonly MethodInfo SetPublicRedirectMethod = typeof(IdentityReadController)
        .Assembly.GetType("Program", throwOnError: true)!
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name.Contains("SetPublicRedirect", StringComparison.Ordinal));

    [Theory]
    [InlineData("Microsoft", "ExternalAuth:PublicOrigin")]
    [InlineData("Microsoft", "ExternalAuth:Microsoft:TenantId")]
    [InlineData("Microsoft", "ExternalAuth:Microsoft:ClientId")]
    [InlineData("Microsoft", "ExternalAuth:Microsoft:ClientSecret")]
    [InlineData("Google", "ExternalAuth:PublicOrigin")]
    [InlineData("Google", "ExternalAuth:Google:WorkspaceDomain")]
    [InlineData("Google", "ExternalAuth:Google:ClientId")]
    [InlineData("Google", "ExternalAuth:Google:ClientSecret")]
    public void Provider_is_not_configured_when_a_required_value_is_missing(
        string provider,
        string missingKey
    )
    {
        Dictionary<string, string?> values = CompleteConfiguration();
        values.Remove(missingKey);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        Assert.False(IsConfigured(configuration, provider));
    }

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Google")]
    public void Provider_is_configured_when_all_required_values_are_present(string provider)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();

        Assert.True(IsConfigured(configuration, provider));
    }

    [Theory]
    [InlineData("Microsoft", "select_account")]
    [InlineData("Google", null)]
    public async Task Public_redirect_events_use_the_configured_origin_and_provider(
        string provider,
        string? prompt
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();
        OpenIdConnectOptions options = new();
        SetPublicRedirectMethod.Invoke(null, [options, configuration, provider.ToLowerInvariant()]);
        AuthenticationScheme scheme = new(provider, provider, typeof(OpenIdConnectHandler));
        DefaultHttpContext http = new();
        AuthenticationProperties properties = new();
        RedirectContext redirect = new(http, scheme, options, properties);
        redirect.ProtocolMessage = new OpenIdConnectMessage();

        await options.Events.OnRedirectToIdentityProvider(redirect);

        Assert.Equal(
            $"https://restaurant.example.test/api/identity/auth/callback/{provider.ToLowerInvariant()}",
            redirect.ProtocolMessage.RedirectUri
        );
        Assert.Equal(prompt, redirect.ProtocolMessage.Prompt);

        AuthorizationCodeReceivedContext code = new(http, scheme, options, properties)
        {
            TokenEndpointRequest = new OpenIdConnectMessage(),
        };
        await options.Events.OnAuthorizationCodeReceived(code);
        Assert.Equal(redirect.ProtocolMessage.RedirectUri, code.TokenEndpointRequest.RedirectUri);
    }

    [Theory]
    [InlineData("/pos/", "/pos/#login_error=provider")]
    [InlineData("/untrusted/", "/backoffice/#login_error=provider")]
    [InlineData(null, "/backoffice/#login_error=provider")]
    public async Task Provider_failure_redirects_only_to_an_allowed_local_pwa_path(
        string? returnPath,
        string expectedLocation
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();
        OpenIdConnectOptions options = new();
        SetPublicRedirectMethod.Invoke(null, [options, configuration, "Google"]);
        AuthenticationScheme scheme = new("Google", "Google", typeof(OpenIdConnectHandler));
        DefaultHttpContext http = new();
        AuthenticationProperties properties = new();
        if (returnPath is not null)
        {
            properties.Items["returnPath"] = returnPath;
        }
        RemoteFailureContext failure = new(
            http,
            scheme,
            options,
            new InvalidOperationException("Provider rejected the request.")
        )
        {
            Properties = properties,
        };

        await options.Events.OnRemoteFailure(failure);

        Assert.Equal(expectedLocation, http.Response.Headers.Location);
        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
    }

    private static bool IsConfigured(IConfiguration configuration, string provider) =>
        (bool)ConfiguredMethod.Invoke(null, [configuration, provider])!;

    private static Dictionary<string, string?> CompleteConfiguration() =>
        new()
        {
            ["ExternalAuth:PublicOrigin"] = "https://restaurant.example.test/",
            ["ExternalAuth:Microsoft:TenantId"] = "tenant-id",
            ["ExternalAuth:Microsoft:ClientId"] = "microsoft-client",
            ["ExternalAuth:Microsoft:ClientSecret"] = "microsoft-secret",
            ["ExternalAuth:Google:WorkspaceDomain"] = "example.test",
            ["ExternalAuth:Google:ClientId"] = "google-client",
            ["ExternalAuth:Google:ClientSecret"] = "google-secret",
        };
}
