using System.Net;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeMaintenanceModulesCoverageTests : BunitContext
{
    private readonly RecordingHandler handler = new();

    public BackofficeMaintenanceModulesCoverageTests()
    {
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
    }

    [Fact]
    public async Task Tax_module_saves_new_tax_and_reports_existing_tax_failure()
    {
        Guid taxId = Guid.NewGuid();
        CatalogDesignSnapshot snapshot = new()
        {
            Taxes =
            [
                new CatalogTaxModel
                {
                    Id = taxId,
                    Code = "IVA",
                    Name = "IVA",
                    Rate = 10m,
                },
            ],
        };
        handler.Responses.Enqueue(Json(snapshot));
        handler.Responses.Enqueue(new(HttpStatusCode.NoContent));
        handler.Responses.Enqueue(Json(snapshot));
        handler.Responses.Enqueue(new(HttpStatusCode.BadRequest));

        IRenderedComponent<TaxesModule> cut = Render<TaxesModule>(parameters =>
            parameters.Add(module => module.Host, Host())
        );
        cut.WaitForAssertion(() => Assert.Contains("IVA", cut.Markup));
        await cut.InvokeAsync(() => cut.Find("button").Click());

        Assert.Equal("PUT", handler.Requests[1].Method);
        Assert.StartsWith("/api/catalog/design/taxes/", handler.Requests[1].Path);
        Assert.Contains("Impuesto guardado.", cut.Markup);

        await cut.InvokeAsync(() => cut.FindAll("button")[^1].Click());

        Assert.Contains("API 400", cut.Markup);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Allergen_module_saves_new_allergen_and_reports_existing_save_failure()
    {
        Guid allergenId = Guid.NewGuid();
        CatalogDesignSnapshot snapshot = new()
        {
            Allergens =
            [
                new CatalogAllergenModel
                {
                    Id = allergenId,
                    Code = "GLUTEN",
                    Name = "Gluten",
                    Description = "Cereales",
                },
            ],
        };
        handler.Responses.Enqueue(Json(snapshot));
        handler.Responses.Enqueue(new(HttpStatusCode.NoContent));
        handler.Responses.Enqueue(Json(snapshot));
        handler.Responses.Enqueue(new(HttpStatusCode.BadRequest));

        IRenderedComponent<AllergensModule> cut = Render<AllergensModule>(parameters =>
            parameters.Add(module => module.Host, Host())
        );
        cut.WaitForAssertion(() => Assert.Contains("Gluten", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("button").Click());

        Assert.Contains("Alérgeno guardado.", cut.Markup);
        Assert.StartsWith("/api/catalog/design/allergens/", handler.Requests[1].Path);

        await cut.InvokeAsync(() => cut.FindAll("button")[^1].Click());

        Assert.Contains("API 400", cut.Markup);
        Assert.Equal(4, handler.Requests.Count);
    }

    private static BackofficeHome Host()
    {
        BackofficeHome home = new();
        typeof(BackofficeHome)
            .GetField("login", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                home,
                new LoginResponse(
                    "token",
                    DateTime.UtcNow.AddHours(1),
                    new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                    [],
                    ["Admin"],
                    ["catalog.global.manage"]
                )
            );
        return home;
    }

    private static HttpResponseMessage Json(CatalogDesignSnapshot snapshot) =>
        new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(snapshot) };

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
