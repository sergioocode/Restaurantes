using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Navigations;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeStartupCoverageTests : BunitContext
{
    private readonly StartupHandler handler = new();
    private readonly BackofficeApi api;

    public BackofficeStartupCoverageTests()
    {
        Services.AddSyncfusionBlazor();
        api = new(new HttpClient(handler) { BaseAddress = new Uri("https://test.local") });
        Services.AddSingleton(api);
        Services.AddSingleton(new SessionAuthenticationStateProvider());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Login_error_fragment_is_reported_after_provider_settings_load()
    {
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.BaseUri + "#other=value&login_error=access_denied");

        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Equal(
            "La cuenta no está autorizada o el proveedor rechazó el acceso.",
            GetField<string?>(cut.Instance, "message")
        );
        Assert.Contains("/api/identity/auth/provider", handler.Paths);
        Assert.Null(api.AccessToken);
    }

    [Fact]
    public void Expired_session_is_removed_and_not_restored_as_authenticated()
    {
        LoginResponse expiredLogin = new(
            "expired-token",
            DateTime.UtcNow.AddMinutes(-1),
            new LoginUser(Guid.NewGuid(), "admin", "Admin"),
            [],
            ["Admin"],
            ["backoffice.access"]
        );
        JSInterop
            .Setup<string?>("sessionStorage.getItem", _ => true)
            .SetResult(JsonSerializer.Serialize(expiredLogin));

        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Null(api.AccessToken);
        Assert.Null(GetField<LoginResponse?>(cut.Instance, "login"));
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "sessionStorage.removeItem"
        );
        Assert.Equal(["/api/identity/auth/provider"], handler.Paths);
    }

    [Fact]
    public async Task Logout_removes_persisted_session_and_clears_authentication_state()
    {
        LoginResponse login = new(
            "active-token",
            DateTime.UtcNow.AddHours(1),
            new LoginUser(Guid.NewGuid(), "admin", "Admin"),
            [],
            ["Admin"],
            ["backoffice.access"]
        );
        api.AccessToken = login.AccessToken;
        SessionAuthenticationStateProvider authState =
            Services.GetRequiredService<SessionAuthenticationStateProvider>();
        authState.SetSession(login);
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(cut.Instance, "login", login);
        SetField(cut.Instance, "message", "Sesión activa.");

        await cut.InvokeAsync(() => Invoke<Task>(cut.Instance, "Logout"));

        Assert.Null(api.AccessToken);
        Assert.Null(GetField<LoginResponse?>(cut.Instance, "login"));
        Assert.Null(GetField<string?>(cut.Instance, "message"));
        Assert.Contains(
            JSInterop.Invocations,
            invocation =>
                invocation.Identifier == "sessionStorage.removeItem"
                && invocation.Arguments.Single()?.ToString() == "restaurantes.backoffice.login"
        );
        AuthenticationState authentication = await authState.GetAuthenticationStateAsync();
        Assert.False(authentication.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public void Start_login_uses_the_selected_provider_and_backoffice_return_path()
    {
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        InvokeAction(cut.Instance, "StartLogin", "Google");

        Assert.EndsWith(
            "/api/identity/auth/start/Google?returnPath=%2Fbackoffice%2F",
            Services.GetRequiredService<NavigationManager>().Uri
        );
    }

    [Theory]
    [InlineData("catalog-design")]
    [InlineData("taxes")]
    [InlineData("allergens")]
    public async Task Refresh_current_module_is_safe_before_specialized_module_mounts(string module)
    {
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));
        SetField(cut.Instance, "module", module);

        await cut.InvokeAsync(() => Invoke<Task>(cut.Instance, "RefreshCurrentModule"));

        Assert.Equal(module, GetField<string>(cut.Instance, "module"));
    }

    [Fact]
    public async Task Navigation_cancels_group_nodes_and_selects_leaf_modules()
    {
        IRenderedComponent<BackofficeHome> cut = Render<BackofficeHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        NodeSelectEventArgs group = Selection("group-products");
        await cut.InvokeAsync(() => InvokeAction(cut.Instance, "BeforeNavigationSelect", group));
        Assert.True(group.Cancel);
        await cut.InvokeAsync(() => Invoke<Task>(cut.Instance, "OnNavigationSelected", group));
        Assert.Equal("catalog-design", GetField<string>(cut.Instance, "module"));

        NodeSelectEventArgs module = Selection("companies");
        await cut.InvokeAsync(() => InvokeAction(cut.Instance, "BeforeNavigationSelect", module));
        Assert.False(module.Cancel);
        await cut.InvokeAsync(() => Invoke<Task>(cut.Instance, "OnNavigationSelected", module));
        Assert.Equal("companies", GetField<string>(cut.Instance, "module"));
    }

    private static NodeSelectEventArgs Selection(string id) =>
        new() { NodeData = new NodeData { Id = id } };

    private static T Invoke<T>(object instance, string methodName, params object[] arguments) =>
        (T)
            instance
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, arguments)!;

    private static void InvokeAction(
        object instance,
        string methodName,
        params object[] arguments
    ) =>
        instance
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, arguments);

    private static T GetField<T>(object instance, string name) =>
        (T)
            instance
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(instance)!;

    private static void SetField(object instance, string name, object? value) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private sealed class StartupHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/api/identity/auth/provider")
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(
                            new ProviderSettingsResponse("Microsoft", true, false)
                        ),
                    }
                );
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
