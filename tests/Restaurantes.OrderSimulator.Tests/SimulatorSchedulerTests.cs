using System.Diagnostics;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorSchedulerTests
{
    [Fact]
    public async Task Scheduler_waits_for_every_restaurant_and_respects_global_concurrency()
    {
        Guid firstRestaurant = Guid.NewGuid();
        Guid secondRestaurant = Guid.NewGuid();
        TaskCompletionSource<bool> firstOrder = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> secondOrder = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int started = 0;
        int activeOrder = 0;
        Simulator.RandomOrderScheduler scheduler = new(
            1,
            [firstRestaurant, secondRestaurant],
            new Simulator.SimulatorRandom(17)
        );

        Task<bool> first = scheduler.RunAsync(
            firstRestaurant,
            () => StartOrder(1, firstOrder, ref started, ref activeOrder)
        );
        Task<bool> second = scheduler.RunAsync(
            secondRestaurant,
            () => StartOrder(2, secondOrder, ref started, ref activeOrder)
        );

        Assert.Equal(0, started);
        scheduler.MarkReady(firstRestaurant);
        Assert.Equal(0, started);
        scheduler.MarkReady(secondRestaurant);
        await WaitUntilAsync(() => Volatile.Read(ref started) == 1);
        Assert.False(first.IsCompleted || second.IsCompleted);
        int firstActiveOrder = Volatile.Read(ref activeOrder);
        (firstActiveOrder == 1 ? firstOrder : secondOrder).SetResult(true);
        await (firstActiveOrder == 1 ? first : second).WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        await WaitUntilAsync(() => Volatile.Read(ref started) == 2);
        int secondActiveOrder = Volatile.Read(ref activeOrder);
        (secondActiveOrder == 1 ? firstOrder : secondOrder).SetResult(false);

        bool[] results = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }

    [Fact]
    public async Task Scheduler_propagates_order_exceptions_and_starts_the_next_waiting_order()
    {
        Guid restaurant = Guid.NewGuid();
        TaskCompletionSource<bool> waitingOrder = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> failingOrder = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int started = 0;
        int activeOrder = 0;
        Simulator.RandomOrderScheduler scheduler = new(
            1,
            [restaurant],
            new Simulator.SimulatorRandom(17)
        );
        Task<bool> failed = scheduler.RunAsync(
            restaurant,
            () => StartOrder(1, failingOrder, ref started, ref activeOrder)
        );
        Task<bool> waiting = scheduler.RunAsync(
            restaurant,
            () => StartOrder(2, waitingOrder, ref started, ref activeOrder)
        );
        scheduler.MarkReady(restaurant);

        await WaitUntilAsync(() => Volatile.Read(ref started) == 1);
        int firstActiveOrder = Volatile.Read(ref activeOrder);
        (firstActiveOrder == 1 ? failingOrder : waitingOrder).SetException(
            new InvalidOperationException("order failed")
        );
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await (firstActiveOrder == 1 ? failed : waiting).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken
                )
        );
        Assert.Equal("order failed", error.Message);
        await WaitUntilAsync(() => Volatile.Read(ref started) == 2);
        int secondActiveOrder = Volatile.Read(ref activeOrder);
        (secondActiveOrder == 1 ? failingOrder : waitingOrder).SetResult(true);
        Assert.True(
            await (secondActiveOrder == 1 ? failed : waiting).WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            )
        );
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(
                timer.Elapsed < TimeSpan.FromSeconds(5),
                "The expected order did not start."
            );
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static Task<bool> StartOrder(
        int orderId,
        TaskCompletionSource<bool> completion,
        ref int started,
        ref int activeOrder
    )
    {
        Interlocked.Exchange(ref activeOrder, orderId);
        Interlocked.Increment(ref started);
        return completion.Task;
    }
}
