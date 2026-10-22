using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Backoffice.Pwa.Components;
using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Backoffice.Pwa.Security;
using Syncfusion.Blazor;
using Xunit;
using BackofficeHome = Restaurantes.Clients.Backoffice.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficeCompanyModulesCoverageTests : BunitContext
{
    private readonly RecordingHandler handler = new();

    public BackofficeCompanyModulesCoverageTests()
    {
        Services.AddSyncfusionBlazor();
        Services.AddSingleton(
            new BackofficeApi(
                new HttpClient(handler) { BaseAddress = new Uri("https://test.local") }
            )
        );
        Services.AddSingleton(new SessionAuthenticationStateProvider());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Company_module_saves_company_and_renders_success_message()
    {
        Guid companyId = Guid.NewGuid();
        CompanyResponse company = new()
        {
            Id = companyId,
            LegalName = "Restaurantes Norte",
            TaxId = "B12345678",
            FiscalAddress = "Calle Mayor 1",
            Phone = "600123123",
            Email = "admin@norte.test",
            Version = 2,
        };
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                [],
                ["Admin"],
                ["identity.manage"]
            ),
            companies: [company]
        );
        IRenderedComponent<CompaniesModule> cut = Render<CompaniesModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );
        cut.Find(".company-settings input").Change("Restaurantes Norte SL");
        handler.Responses.Enqueue(Json(company));
        await cut.Find(".company-settings button[aria-label='Guardar empresa']").ClickAsync();

        Assert.Equal("Restaurantes Norte SL", company.LegalName);
        Assert.Contains("Empresa actualizada.", GetField<string?>(host, "message"));
        Assert.Equal("PUT", handler.Requests.Single().Method);
        Assert.Equal(
            $"/api/restaurant-operations/companies/{companyId}",
            handler.Requests.Single().Path
        );
    }

    [Fact]
    public async Task Restaurant_module_limits_rows_to_the_manager_restaurant()
    {
        Guid visibleId = Guid.NewGuid();
        Guid hiddenId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "manager", "Gerente"),
                [new RestaurantAccess(visibleId, "Gerente", ["backoffice.access", "tables.manage"])]
            ),
            restaurants:
            [
                new RestaurantResponse
                {
                    Id = visibleId,
                    Code = "NORTE",
                    Name = "Local Norte",
                    Address = "Calle 1",
                    CompanyId = companyId,
                    IsActive = true,
                },
                new RestaurantResponse
                {
                    Id = hiddenId,
                    Code = "SUR",
                    Name = "Local Sur",
                    Address = "Calle 2",
                    CompanyId = companyId,
                    IsActive = true,
                },
            ],
            companies:
            [
                new CompanyResponse
                {
                    Id = companyId,
                    LegalName = "Cadena",
                    TaxId = "B12345678",
                },
            ]
        );
        SetField(host, "restaurantId", visibleId);

        IRenderedComponent<RestaurantsModule> cut = Render<RestaurantsModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );

        Assert.Contains("Local Norte", cut.Markup);
        Assert.DoesNotContain("Local Sur", cut.Markup);
        Assert.Contains("Nuevo local", cut.Markup);
        Assert.Single(cut.FindAll(".restaurant-settings"));
        Assert.False(cut.Find(".restaurant-settings input").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Restaurant_active_state_uses_server_result_and_rolls_back_on_failure()
    {
        Guid restaurantId = Guid.NewGuid();
        RestaurantResponse restaurant = new()
        {
            Id = restaurantId,
            Code = "NORTE",
            Name = "Local Norte",
            IsActive = true,
            Version = 1,
        };
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                [],
                ["Admin"]
            ),
            restaurants: [restaurant]
        );
        IRenderedComponent<RestaurantsModule> cut = Render<RestaurantsModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );
        handler.Responses.Enqueue(Json(new RestaurantResponse { IsActive = false, Version = 2 }));

        await cut.Find(".restaurant-settings .availability input").ChangeAsync(false);

        Assert.False(restaurant.IsActive);
        Assert.Equal(
            ("PUT", $"/api/restaurant-operations/restaurants/{restaurantId}/active"),
            handler.Requests.Last()
        );
        Assert.Contains("Estado del local actualizado.", GetField<string?>(host, "message"));

        handler.Responses.Enqueue(
            new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"title\":\"No se pudo actualizar\"}"),
            }
        );
        await cut.Find(".restaurant-settings .availability input").ChangeAsync(true);

        Assert.False(restaurant.IsActive);
        Assert.Contains("API 400", GetField<string?>(host, "message"));
    }

    [Fact]
    public async Task Company_module_hides_company_data_when_user_has_no_chain_role()
    {
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "staff", "Camarero"),
                [],
                [],
                ["backoffice.access"]
            ),
            companies: [new CompanyResponse { LegalName = "Empresa privada" }]
        );

        IRenderedComponent<CompaniesModule> cut = Render<CompaniesModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );

        Assert.Contains("Sin acceso de edición", cut.Markup);
        Assert.DoesNotContain("Empresa privada", cut.Markup);
    }

    [Fact]
    public async Task Users_module_blocks_an_email_already_authorized_in_another_restaurant()
    {
        Guid restaurantId = Guid.NewGuid();
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                [],
                ["Admin"],
                ["identity.manage"]
            ),
            restaurants:
            [
                new RestaurantResponse
                {
                    Id = restaurantId,
                    Code = "NORTE",
                    Name = "Local Norte",
                    IsActive = true,
                },
            ]
        );
        SetField(host, "restaurantId", restaurantId);
        SetField(
            host,
            "authorizedUsers",
            new List<StaffUserResponse>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Email = "persona@example.test",
                    RestaurantId = Guid.NewGuid(),
                },
            }
        );
        GetField<StaffUserDraft>(host, "userDraft").Email = " Persona@Example.Test ";

        IRenderedComponent<UsersModule> cut = Render<UsersModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );

        Assert.Contains("ya está autorizado", cut.Markup);
        Assert.True(cut.Find(".user-create-form button").HasAttribute("disabled"));
        cut.Find(".user-create-form input[type='email']")
            .TriggerEvent("oninput", new ChangeEventArgs { Value = "nuevo@example.test" });
        Assert.False(cut.Find(".user-create-form button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Daily_sales_module_orders_sales_shows_context_and_opens_the_ticket()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableOrderId = Guid.NewGuid();
        Guid customerOrderId = Guid.NewGuid();
        DateTime olderSaleAt = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime newerSaleAt = olderSaleAt.AddHours(1);
        SaleResponse olderSale = new(
            Guid.NewGuid(),
            restaurantId,
            "POS",
            "Cash",
            12m,
            "Completed",
            1,
            olderSaleAt,
            [customerOrderId],
            []
        );
        SaleResponse newerSale = new(
            Guid.NewGuid(),
            restaurantId,
            "QR",
            "Card",
            18m,
            "Completed",
            1,
            newerSaleAt,
            [tableOrderId],
            []
        );
        SaleResponse saleWithoutContext = new(
            Guid.NewGuid(),
            restaurantId,
            "POS",
            "Cash",
            7m,
            "Completed",
            2,
            newerSaleAt.AddHours(1),
            [],
            []
        );
        BackofficeHome host = await CreateHost(
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "admin", "Admin"),
                [],
                ["Admin"]
            ),
            restaurants:
            [
                new RestaurantResponse
                {
                    Id = restaurantId,
                    Code = "NORTE",
                    Name = "Local Norte",
                    IsActive = true,
                },
            ]
        );
        SetField(
            host,
            "historySales",
            new List<SaleResponse> { olderSale, newerSale, saleWithoutContext }
        );
        SetField(
            host,
            "historyOrders",
            new List<ManagementOrderResponse>
            {
                new(
                    tableOrderId,
                    restaurantId,
                    "Mesa 12",
                    "Cliente mesa",
                    "POS",
                    "Paid",
                    18m,
                    newerSaleAt,
                    newerSaleAt,
                    "Card",
                    newerSaleAt,
                    []
                ),
                new(
                    customerOrderId,
                    restaurantId,
                    string.Empty,
                    "Cliente QR",
                    "QR",
                    "Paid",
                    12m,
                    olderSaleAt,
                    olderSaleAt,
                    "Cash",
                    olderSaleAt,
                    []
                ),
            }
        );

        IRenderedComponent<DailySalesModule> cut = Render<DailySalesModule>(parameters =>
            parameters.Add(module => module.Host, host)
        );
        IReadOnlyList<AngleSharp.Dom.IElement> rows = cut.FindAll(".order-row");

        Assert.Equal(3, rows.Count);
        Assert.Contains("2 comandas", rows[0].TextContent);
        Assert.Contains("Mesa 12", rows[1].TextContent);
        Assert.Contains("Local Norte", rows[1].TextContent);
        Assert.Contains("Cliente QR", rows[2].TextContent);
        await cut.Find("button[aria-label='Ver ticket de venta']").ClickAsync();

        Assert.Same(saleWithoutContext, GetField<SaleResponse?>(host, "selectedSale"));
        await cut.Find("input[type='date']").ChangeAsync("2026-10-22");
        Assert.Equal(new DateOnly(2026, 10, 22), GetField<DateOnly>(host, "historyDate"));
    }

    [Fact]
    public async Task Daily_sales_module_shows_empty_state_only_after_loading_finishes()
    {
        LoginResponse login = new(
            "token",
            DateTime.UtcNow.AddHours(1),
            new LoginUser(Guid.NewGuid(), "admin", "Admin"),
            [],
            ["Admin"]
        );
        BackofficeHome loadedHost = await CreateHost(login);
        IRenderedComponent<DailySalesModule> loaded = Render<DailySalesModule>(parameters =>
            parameters.Add(module => module.Host, loadedHost)
        );

        Assert.Contains("No hay ventas para esta fecha.", loaded.Markup);

        BackofficeHome busyHost = await CreateHost(login);
        SetField(busyHost, "busy", true);
        IRenderedComponent<DailySalesModule> busy = Render<DailySalesModule>(parameters =>
            parameters.Add(module => module.Host, busyHost)
        );

        Assert.DoesNotContain("No hay ventas para esta fecha.", busy.Markup);
    }

    private async Task<BackofficeHome> CreateHost(
        LoginResponse login,
        List<CompanyResponse>? companies = null,
        List<RestaurantResponse>? restaurants = null
    )
    {
        IRenderedComponent<BackofficeHome> rendered = Render<BackofficeHome>();
        rendered.WaitForState(() => !GetField<bool>(rendered.Instance, "initializing"));
        BackofficeHome host = rendered.Instance;
        SetField(host, "login", login);
        SetField(host, "companies", companies ?? []);
        SetField(host, "restaurants", restaurants ?? []);
        Services.GetRequiredService<BackofficeApi>().AccessToken = login.AccessToken;
        handler.Requests.Clear();
        await rendered.InvokeAsync(() => Task.CompletedTask);
        return host;
    }

    private static void SetField(object target, string name, object value) =>
        typeof(BackofficeHome)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T GetField<T>(object target, string name) =>
        (T)
            typeof(BackofficeHome)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(target)!;

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

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
