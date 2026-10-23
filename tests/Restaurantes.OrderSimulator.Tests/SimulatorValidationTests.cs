using System.Diagnostics;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorValidationTests
{
    private static readonly Guid RestaurantId = Guid.Parse("9e9e98b8-ec1a-424b-b5f5-f4e093ee4db0");

    [Fact]
    public void Valid_local_configuration_is_accepted()
    {
        Simulator.Validate(Options());
    }

    [Fact]
    public void Order_generation_stops_when_disabled_cancelled_or_out_of_time()
    {
        Stopwatch timer = Stopwatch.StartNew();
        TimeSpan duration = TimeSpan.FromMinutes(1);
        using CancellationTokenSource cancellation = new();

        Assert.True(Simulator.CanCreateMoreOrders(true, timer, duration, cancellation.Token));
        Assert.False(Simulator.CanCreateMoreOrders(false, timer, duration, cancellation.Token));
        Assert.False(Simulator.CanCreateMoreOrders(true, timer, TimeSpan.Zero, cancellation.Token));
        cancellation.Cancel();
        Assert.False(Simulator.CanCreateMoreOrders(true, timer, duration, cancellation.Token));
    }

    [Fact]
    public async Task Completed_orders_are_counted_and_removed_without_touching_active_orders()
    {
        Guid completedId = Guid.NewGuid();
        Guid failedId = Guid.NewGuid();
        Guid activeId = Guid.NewGuid();
        TaskCompletionSource<bool> active = new();
        Dictionary<Guid, Task<bool>> orders = new()
        {
            [completedId] = Task.FromResult(true),
            [failedId] = Task.FromResult(false),
            [activeId] = active.Task,
        };

        (int completed, int failed) = await Simulator.CollectCompletedOrdersAsync(orders);

        Assert.Equal(1, completed);
        Assert.Equal(1, failed);
        Assert.Equal(active.Task, Assert.Single(orders).Value);
        Assert.True(orders.ContainsKey(activeId));
    }

    [Fact]
    public async Task Waiting_for_order_activity_resumes_when_an_active_order_finishes()
    {
        TaskCompletionSource<bool> active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dictionary<Guid, Task<bool>> orders = new() { [Guid.NewGuid()] = active.Task };

        Task waiting = Simulator.WaitForOrderActivityAsync(orders, CancellationToken.None);

        Assert.False(waiting.IsCompleted);
        active.SetResult(true);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancellation_waits_for_active_orders_to_finish()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        TaskCompletionSource<bool> active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dictionary<Guid, Task<bool>> orders = new() { [Guid.NewGuid()] = active.Task };

        Task waiting = Simulator.WaitForOrderActivityAsync(orders, cancellation.Token);

        Assert.False(waiting.IsCompleted);
        active.SetResult(true);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancellation_with_no_active_orders_stops_the_poll()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Simulator.WaitForOrderActivityAsync([], cancellation.Token)
        );
    }

    [Fact]
    public void Invalid_global_configuration_reports_the_rejected_setting()
    {
        (SimulatorOptions Options, string Expected)[] cases =
        [
            (Options(apiBaseUrl: "invalid"), "URL absoluta"),
            (Options(apiBaseUrl: "file:///tmp/orders"), "HTTP o HTTPS"),
            (Options(apiBaseUrl: "https://example.com"), "no es local"),
            (Options(restaurants: []), "al menos un local"),
            (Options(restaurants: [Restaurant(), Restaurant()]), "RestaurantId distinto"),
            (Options(ordersPerBatch: 0), "OrdersPerBatch"),
            (Options(maxConcurrentOrders: 0), "MaxConcurrentOrders"),
            (Options(runDurationMinutes: 0), "RunDurationMinutes"),
            (Options(kitchenStepDelaySeconds: 31), "KitchenStepDelaySeconds"),
        ];

        foreach ((SimulatorOptions options, string expected) in cases)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                Simulator.Validate(options)
            );
            Assert.Contains(expected, error.Message);
        }
    }

    [Fact]
    public void Invalid_restaurant_configuration_reports_the_rejected_setting()
    {
        (RestaurantOptions Restaurant, string Expected)[] cases =
        [
            (Restaurant(id: Guid.Empty), "Name y RestaurantId"),
            (Restaurant(name: " "), "Name y RestaurantId"),
            (Restaurant(posAccessToken: " "), "PosAccessToken"),
            (Restaurant(kdsAccessToken: " "), "KdsAccessToken"),
            (Restaurant(openingFloat: -1), "OpeningFloat"),
            (Restaurant(guestCount: new IntRange(1, 10)), "GuestCount"),
            (Restaurant(maxProductsPerOrder: 0), "MaxProductsPerOrder"),
            (Restaurant(payment: new PaymentOptions { Methods = [] }), "métodos Card"),
            (
                Restaurant(payment: new PaymentOptions { SplitProbability = 2 }),
                "probabilidades entre 0 y 1"
            ),
            (
                Restaurant(
                    payment: new PaymentOptions
                    {
                        Methods =
                        [
                            new() { Name = "Card" },
                            new() { Name = "Cash" },
                            new() { Name = "Card" },
                        ],
                    }
                ),
                "no repitas métodos de pago"
            ),
        ];

        foreach ((RestaurantOptions restaurant, string expected) in cases)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                Simulator.Validate(Options(restaurants: [restaurant]))
            );
            Assert.Contains(expected, error.Message);
        }
    }

    [Fact]
    public void Invalid_payment_configuration_reports_each_rejected_setting()
    {
        (PaymentOptions Payment, string Expected)[] cases =
        [
            (
                new PaymentOptions
                {
                    Methods =
                    [
                        new() { Name = "Card", Weight = 0 },
                        new() { Name = "Cash", Weight = 1 },
                        new() { Name = "Transfer", Weight = 1 },
                    ],
                },
                "métodos Card"
            ),
            (
                new PaymentOptions
                {
                    Methods =
                    [
                        new() { Name = "Card", Weight = 1 },
                        new() { Name = "Cash", Weight = 1 },
                        new() { Name = "Crypto", Weight = 1 },
                    ],
                },
                "métodos Card"
            ),
            (new PaymentOptions { CashOverpayProbability = -0.1 }, "probabilidades entre 0 y 1"),
            (new PaymentOptions { CashOverpayProbability = 1.1 }, "probabilidades entre 0 y 1"),
            (new PaymentOptions { TipPercentages = [] }, "probabilidades entre 0 y 1"),
            (new PaymentOptions { TipPercentages = [0] }, "probabilidades entre 0 y 1"),
            (new PaymentOptions { TipPercentages = [101] }, "probabilidades entre 0 y 1"),
        ];

        foreach ((PaymentOptions payment, string expected) in cases)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                Simulator.Validate(Options(restaurants: [Restaurant(payment: payment)]))
            );
            Assert.Contains(expected, error.Message);
        }
    }

    private static SimulatorOptions Options(
        string apiBaseUrl = "http://localhost:5001",
        List<RestaurantOptions>? restaurants = null,
        int ordersPerBatch = 20,
        int maxConcurrentOrders = 10,
        int runDurationMinutes = 10,
        int kitchenStepDelaySeconds = 2
    ) =>
        new()
        {
            ApiBaseUrl = apiBaseUrl,
            Restaurants = restaurants ?? [Restaurant()],
            OrdersPerBatch = ordersPerBatch,
            MaxConcurrentOrders = maxConcurrentOrders,
            RunDurationMinutes = runDurationMinutes,
            KitchenStepDelaySeconds = kitchenStepDelaySeconds,
        };

    private static RestaurantOptions Restaurant(
        Guid? id = null,
        string name = "Local de prueba",
        decimal openingFloat = 0,
        IntRange? guestCount = null,
        int maxProductsPerOrder = 4,
        PaymentOptions? payment = null,
        string posAccessToken = "pos-token",
        string kdsAccessToken = "kds-token"
    ) =>
        new()
        {
            RestaurantId = id ?? RestaurantId,
            Name = name,
            PosAccessToken = posAccessToken,
            KdsAccessToken = kdsAccessToken,
            OpeningFloat = openingFloat,
            GuestCount = guestCount ?? new IntRange(2, 10),
            MaxProductsPerOrder = maxProductsPerOrder,
            Payment = payment ?? new PaymentOptions(),
        };
}
