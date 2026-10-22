using System.Net;
using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Syncfusion.Blazor;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeTablesModuleCoverageTests : BunitContext
{
    private readonly RecordingHandler handler = new();
    private readonly Guid restaurantId = Guid.NewGuid();
    private readonly Guid zoneId = Guid.NewGuid();
    private readonly Guid secondZoneId = Guid.NewGuid();
    private readonly Guid tableId = Guid.NewGuid();

    public BackofficeTablesModuleCoverageTests()
    {
        Services.AddSyncfusionBlazor();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(new SessionAuthenticationStateProvider());
    }

    [Fact]
    public async Task Saves_table_label_and_zone()
    {
        IRenderedComponent<TablesModule> cut = RenderModule();
        EnqueueSuccessfulUpdate();

        await cut.InvokeAsync(() => cut.Find(".table-name-editor input").Change("Mesa salón"));
        await cut.InvokeAsync(() =>
            cut.Find("button[aria-label='Guardar nombre de mesa']").Click()
        );
        await cut.InvokeAsync(() =>
            cut.FindAll("select[aria-label='Zona']")[^1].Change(secondZoneId.ToString())
        );

        Assert.True(handler.Requests.Count == 4, string.Join(", ", handler.Requests));
        Assert.Equal("PUT", handler.Requests[0].Method);
        Assert.Equal($"/api/dining/tables/{tableId}", handler.Requests[0].Path);
        Assert.Equal("PUT", handler.Requests[2].Method);
        Assert.Equal($"/api/dining/tables/{tableId}", handler.Requests[2].Path);
    }

    [Fact]
    public async Task Restores_table_active_state_when_save_fails()
    {
        IRenderedComponent<TablesModule> cut = RenderModule();
        handler.Responses.Enqueue(
            new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"title\":\"test failure\"}"),
            }
        );
        await cut.InvokeAsync(() => cut.Find("input[type='checkbox']").Change(false));

        Assert.True(cut.Find("input[type='checkbox']").HasAttribute("checked"));
        Assert.Contains("API 400", GetField<string?>(cut.Instance.Host, "message"));
        Assert.Single(handler.Requests);
    }

    private IRenderedComponent<TablesModule> RenderModule()
    {
        TableResponse table = new()
        {
            Id = tableId,
            RestaurantId = restaurantId,
            ZoneId = zoneId,
            Code = "M09",
            Label = "Mesa 09",
            QrCode = "qr-code",
            CustomerQrPath = "/qr/M09",
            IsActive = true,
        };
        IRenderedComponent<BackofficeHome> home = Render<BackofficeHome>();
        home.WaitForState(() => !GetField<bool>(home.Instance, "initializing"));
        BackofficeHome host = home.Instance;
        SetField(
            host,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                [],
                ["Admin"],
                ["tables.manage"]
            )
        );
        SetField(host, "restaurantId", restaurantId);
        SetField(host, "tables", new List<TableResponse> { table });
        SetField(
            host,
            "zones",
            new List<ZoneResponse>
            {
                new() { Id = zoneId, Name = "Salón" },
                new()
                {
                    Id = secondZoneId,
                    Name = "Terraza",
                    SortOrder = 1,
                },
            }
        );
        Services.GetRequiredService<BackofficeApi>().AccessToken = "token";
        handler.Requests.Clear();

        return Render<TablesModule>(parameters => parameters.Add(module => module.Host, host));
    }

    private void EnqueueSuccessfulUpdate()
    {
        TableResponse table = new()
        {
            Id = tableId,
            RestaurantId = restaurantId,
            ZoneId = zoneId,
            Code = "M09",
            Label = "Mesa salón",
            QrCode = "qr-code",
            CustomerQrPath = "/qr/M09",
            IsActive = true,
        };
        for (int index = 0; index < 2; index++)
        {
            handler.Responses.Enqueue(Json(table));
            handler.Responses.Enqueue(Json(new List<TableResponse> { table }));
        }
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(value) };

    private static void SetField(object target, string name, object value) =>
        typeof(BackofficeHome)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T GetField<T>(object target, string name) =>
        (T)
            typeof(BackofficeHome)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(target)!;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Method, string Path)> Requests { get; } = [];
        public Queue<HttpResponseMessage> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(
                Responses.Count > 0
                    ? Responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.NoContent)
            );
        }
    }
}
