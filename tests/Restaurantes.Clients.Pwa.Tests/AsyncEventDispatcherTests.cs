using System.Reflection;
using Restaurantes.Clients.Shared.Operations;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class AsyncEventDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_awaits_each_subscriber_in_registration_order()
    {
        List<int> values = [];
        Func<int, Task> handlers = async value =>
        {
            await Task.Yield();
            values.Add(value);
        };
        handlers += value =>
        {
            values.Add(value * 2);
            return Task.CompletedTask;
        };

        await AsyncEventDispatcher.InvokeAsync(handlers, 21);

        Assert.Equal([21, 42], values);
    }

    [Fact]
    public async Task InvokeAsync_allows_no_subscribers()
    {
        await AsyncEventDispatcher.InvokeAsync<int>(null, 1);
    }

    [Fact]
    public async Task Commander_realtime_notifications_are_scoped_to_the_current_restaurant()
    {
        using HttpClient http = new();
        await using var client =
            new Restaurantes.Clients.Commander.Pwa.Realtime.OrderRealtimeClient(
                http,
                new Restaurantes.Clients.Commander.Pwa.Api.CommanderApi(http)
            );
        await AssertRealtimeNotificationsAsync(
            client,
            restaurantId => new Restaurantes.Clients.Commander.Pwa.Models.OrderRealtimeNotification(
                Guid.NewGuid(),
                restaurantId,
                "Created",
                1,
                DateTime.UtcNow,
                []
            )
        );
    }

    [Fact]
    public async Task Pos_realtime_notifications_are_scoped_to_the_current_restaurant()
    {
        using HttpClient http = new();
        await using var client = new Restaurantes.Clients.Pos.Pwa.Realtime.OrderRealtimeClient(
            http,
            new Restaurantes.Clients.Pos.Pwa.Api.PosApi(http)
        );
        await AssertRealtimeNotificationsAsync(
            client,
            restaurantId => new Restaurantes.Clients.Pos.Pwa.Models.OrderRealtimeNotification(
                Guid.NewGuid(),
                restaurantId,
                "Created",
                1,
                DateTime.UtcNow,
                []
            )
        );
    }

    [Fact]
    public async Task Backoffice_pagination_stops_after_a_partial_page()
    {
        List<int> requestedOffsets = [];
        Func<int, Task<List<int>>> loadPage = offset =>
        {
            requestedOffsets.Add(offset);
            return Task.FromResult(
                offset == 0 ? Enumerable.Range(0, 100).ToList() : new List<int> { 100, 101 }
            );
        };
        MethodInfo loader = typeof(Restaurantes.Clients.Backoffice.Pwa.Pages.Home).GetMethod(
            "LoadPagesAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Task<List<int>> resultTask =
            (Task<List<int>>)loader.MakeGenericMethod(typeof(int)).Invoke(null, [loadPage])!;

        List<int> result = await resultTask;

        Assert.Equal([0, 100], requestedOffsets);
        Assert.Equal(102, result.Count);
        Assert.Equal(101, result[^1]);
    }

    [Fact]
    public void Commander_payment_keypad_builds_and_validates_values()
    {
        object home = new Restaurantes.Clients.Commander.Pwa.Pages.Home();
        SetField(home, "paymentKeypadStartNew", true);
        SetField(home, "paymentKeypadTarget", "Tip");
        Press(home, "5");
        Press(home, ",");
        Press(home, "2");
        Press(home, ",");

        Assert.Equal("5,2", GetField(home, "paymentKeypadText"));
        Assert.Equal(5.2m, GetProperty(home, "CurrentTipAmount"));

        Press(home, "Borrar");

        Assert.Equal("0", GetField(home, "paymentKeypadText"));
        Assert.Equal(0m, GetProperty(home, "CurrentTipAmount"));
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData("1,234")]
    [InlineData("1000001")]
    public void Commander_payment_keypad_rejects_out_of_range_values(string value)
    {
        object home = new Restaurantes.Clients.Commander.Pwa.Pages.Home();
        SetField(home, "paymentKeypadText", value[..^1]);
        SetField(home, "paymentKeypadTarget", "Tip");
        SetField(home, "paymentKeypadStartNew", false);
        string previousText = (string)GetField(home, "paymentKeypadText")!;
        decimal previousAmount = (decimal)GetProperty(home, "CurrentTipAmount")!;

        Press(home, value[^1..]);

        Assert.Equal(previousText, GetField(home, "paymentKeypadText"));
        Assert.Equal(previousAmount, GetProperty(home, "CurrentTipAmount"));
    }

    [Fact]
    public void Backoffice_filters_select_only_authorized_restaurants_and_search_products()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        var first = new Restaurantes.Clients.Backoffice.Pwa.Models.RestaurantResponse
        {
            Id = firstId,
            Name = "Centro",
        };
        var second = new Restaurantes.Clients.Backoffice.Pwa.Models.RestaurantResponse
        {
            Id = secondId,
            Name = "Norte",
        };
        var home = new Restaurantes.Clients.Backoffice.Pwa.Pages.Home();
        SetField(
            home,
            "restaurants",
            new List<Restaurantes.Clients.Backoffice.Pwa.Models.RestaurantResponse>
            {
                first,
                second,
            }
        );
        SetField(
            home,
            "login",
            new Restaurantes.Clients.Backoffice.Pwa.Models.LoginResponse(
                "token",
                DateTime.UtcNow,
                new Restaurantes.Clients.Backoffice.Pwa.Models.LoginUser(
                    Guid.NewGuid(),
                    "user",
                    "User"
                ),
                [
                    new(
                        firstId,
                        "Gerente",
                        [
                            "identity.manage",
                            "backoffice.access",
                            "orders.read",
                            "orders.report.read",
                        ]
                    ),
                ]
            )
        );

        Assert.Single(GetRestaurants(home, "ManageableRestaurants"));
        Assert.Equal(firstId, GetRestaurants(home, "SelectableRestaurants")[0].Id);
        Assert.Equal(firstId, GetRestaurants(home, "VisibleCompanyRestaurants")[0].Id);
        Assert.Equal(firstId, GetRestaurants(home, "OrderRestaurants")[0].Id);
        Assert.Equal(firstId, GetRestaurants(home, "ReportRestaurants")[0].Id);
        Assert.Equal(firstId, GetRestaurants(home, "ContextRestaurants")[0].Id);

        SetField(home, "module", "daily-orders-report");
        Assert.Equal(firstId, GetRestaurants(home, "ContextRestaurants")[0].Id);
        SetField(
            home,
            "products",
            new List<Restaurantes.Clients.Backoffice.Pwa.Models.ProductResponse>
            {
                new() { Name = "Hamburguesa", Sku = "BUR-01" },
                new() { Name = "Ensalada", Sku = "SAL-02" },
            }
        );
        SetField(home, "search", "sal");
        Assert.Equal("Ensalada", GetProducts(home).Single().Name);
    }

    private static List<Restaurantes.Clients.Backoffice.Pwa.Models.RestaurantResponse> GetRestaurants(
        object home,
        string methodName
    ) =>
        (List<Restaurantes.Clients.Backoffice.Pwa.Models.RestaurantResponse>)
            home.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(home, null)!;

    private static List<Restaurantes.Clients.Backoffice.Pwa.Models.ProductResponse> GetProducts(
        object home
    ) =>
        (List<Restaurantes.Clients.Backoffice.Pwa.Models.ProductResponse>)
            home.GetType()
                .GetMethod("VisibleProducts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(home, null)!;

    private static async Task AssertRealtimeNotificationsAsync<TNotification>(
        object client,
        Func<Guid, TNotification> createNotification
    )
    {
        Guid restaurantId = Guid.NewGuid();
        SetField(client, "restaurantId", restaurantId);
        TNotification matchingNotification = createNotification(restaurantId);
        TNotification otherRestaurantNotification = createNotification(Guid.NewGuid());

        int orderNotifications = 0;
        Func<TNotification, Task> orderHandler = _ =>
        {
            orderNotifications++;
            return Task.CompletedTask;
        };
        client.GetType().GetEvent("OrderUpdated")!.AddEventHandler(client, orderHandler);
        MethodInfo notifyOrder = client
            .GetType()
            .GetMethod("NotifyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)notifyOrder.Invoke(client, [matchingNotification])!;
        await (Task)notifyOrder.Invoke(client, [otherRestaurantNotification])!;
        Assert.Equal(1, orderNotifications);

        int diningNotifications = 0;
        Func<Guid, Task> diningHandler = _ =>
        {
            diningNotifications++;
            return Task.CompletedTask;
        };
        client.GetType().GetEvent("DiningTableUpdated")!.AddEventHandler(client, diningHandler);
        MethodInfo notifyDining = client
            .GetType()
            .GetMethod(
                "NotifyDiningTableUpdatedAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [typeof(Guid)],
                null
            )!;
        await (Task)notifyDining.Invoke(client, [restaurantId])!;
        await (Task)notifyDining.Invoke(client, [Guid.NewGuid()])!;
        Assert.Equal(1, diningNotifications);
    }

    private static void Press(object home, string key) =>
        typeof(Restaurantes.Clients.Commander.Pwa.Pages.Home)
            .GetMethod("PressPaymentKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(home, [key]);

    private static void SetField(object instance, string name, object value) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static object? GetField(object instance, string name) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance);

    private static object? GetProperty(object instance, string name) =>
        instance
            .GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance);
}
