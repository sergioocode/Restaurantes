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
    [InlineData("ExternalAuth:PublicOrigin")]
    [InlineData("ExternalAuth:Keycloak:Authority")]
    [InlineData("ExternalAuth:Keycloak:ClientId")]
    [InlineData("ExternalAuth:Keycloak:ClientSecret")]
    public void Keycloak_is_not_configured_when_a_required_value_is_missing(string missingKey)
    {
        Dictionary<string, string?> values = CompleteConfiguration();
        values.Remove(missingKey);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        Assert.False(IsConfigured(configuration));
    }

    [Fact]
    public void Keycloak_is_configured_when_all_required_values_are_present()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();

        Assert.True(IsConfigured(configuration));
    }

    [Fact]
    public async Task Public_redirect_events_use_the_configured_origin_and_Keycloak_callback()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();
        OpenIdConnectOptions options = new() { ClientId = "identity-client" };
        SetPublicRedirectMethod.Invoke(null, [options, configuration]);
        AuthenticationScheme scheme = new("Keycloak", "Keycloak", typeof(OpenIdConnectHandler));
        DefaultHttpContext http = new();
        AuthenticationProperties properties = new();
        RedirectContext redirect = new(http, scheme, options, properties);
        redirect.ProtocolMessage = new OpenIdConnectMessage();

        await options.Events.OnRedirectToIdentityProvider(redirect);

        Assert.Equal(
            "https://restaurant.example.test/api/identity/auth/callback/keycloak",
            redirect.ProtocolMessage.RedirectUri
        );
        Assert.Null(redirect.ProtocolMessage.Prompt);

        AuthorizationCodeReceivedContext code = new(http, scheme, options, properties)
        {
            TokenEndpointRequest = new OpenIdConnectMessage(),
        };
        await options.Events.OnAuthorizationCodeReceived(code);
        Assert.Equal(redirect.ProtocolMessage.RedirectUri, code.TokenEndpointRequest.RedirectUri);

        RedirectContext signOut = new(http, scheme, options, properties)
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };
        await options.Events.OnRedirectToIdentityProviderForSignOut(signOut);
        Assert.Equal("identity-client", signOut.ProtocolMessage.ClientId);
        Assert.Equal(
            "https://restaurant.example.test/api/identity/auth/logout-callback/keycloak",
            signOut.ProtocolMessage.PostLogoutRedirectUri
        );
    }

    [Fact]
    public async Task Redirect_event_requests_interactive_login_for_account_switch()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfiguration())
            .Build();
        OpenIdConnectOptions options = new();
        SetPublicRedirectMethod.Invoke(null, [options, configuration]);
        AuthenticationScheme scheme = new("Keycloak", "Keycloak", typeof(OpenIdConnectHandler));
        RedirectContext redirect = new(
            new DefaultHttpContext(),
            scheme,
            options,
            new AuthenticationProperties { Items = { ["reauthenticate"] = "true" } }
        )
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };

        await options.Events.OnRedirectToIdentityProvider(redirect);

        Assert.Equal("login", redirect.ProtocolMessage.Prompt);
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
        SetPublicRedirectMethod.Invoke(null, [options, configuration]);
        AuthenticationScheme scheme = new("Keycloak", "Keycloak", typeof(OpenIdConnectHandler));
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

    private static bool IsConfigured(IConfiguration configuration) =>
        (bool)ConfiguredMethod.Invoke(null, [configuration])!;

    private static Dictionary<string, string?> CompleteConfiguration() =>
        new()
        {
            ["ExternalAuth:PublicOrigin"] = "https://restaurant.example.test/",
            ["ExternalAuth:Keycloak:Authority"] =
                "https://keycloak.example.test/realms/restaurants",
            ["ExternalAuth:Keycloak:ClientId"] = "identity-client",
            ["ExternalAuth:Keycloak:ClientSecret"] = "identity-secret",
        };
}
