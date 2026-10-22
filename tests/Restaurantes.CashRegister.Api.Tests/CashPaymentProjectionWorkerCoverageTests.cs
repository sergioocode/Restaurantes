using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.CashRegister.Infrastructure.Stores;
using Restaurantes.CashRegister.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashPaymentProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Captured_payment_is_projected_and_acknowledged_once_even_when_delivered_twice()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        DateTime capturedAt = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        await fixture.AddOpenSessionAsync(restaurantId, sessionId, capturedAt);
        PaymentCaptured payment = new(
            paymentId,
            orderId,
            restaurantId,
            18m,
            "Card",
            "DineIn",
            "Pos",
            "REF-123",
            capturedAt,
            CashRegisterSessionId: sessionId,
            TipAmount: 2m,
            ChangeAmount: 3m,
            SaleGroupId: saleId
        );

        await fixture.DeliverAsync(messageId, payment);
        await fixture.DeliverAsync(messageId, payment);

        List<CashMovement> movements = await fixture.GetMovementsAsync();
        Assert.True(
            movements.Count == 3,
            $"Expected a sale plus two card-change movements; found {movements.Count}. "
                + $"ACKs: {fixture.Channel.Acknowledgements}; retries: {fixture.Channel.RetryPublications}; "
                + $"rejections: {fixture.Channel.Rejections}; error: {fixture.LastException}."
        );
        Assert.Contains(
            movements,
            x => x.Id == messageId && x.Type == CashMovementType.Sale && x.Amount == 20m
        );
        Assert.Contains(movements, x => x.Type == CashMovementType.CardTender && x.Amount == 3m);
        Assert.Contains(movements, x => x.Type == CashMovementType.CashChange && x.Amount == -3m);
        Assert.All(movements, x => Assert.Equal(saleId, x.SaleId));
        Assert.True(await fixture.WasMessageProcessedAsync(messageId));
        Assert.Equal(2, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        Assert.Equal(0, fixture.Channel.RetryPublications);
    }

    [Fact]
    public async Task Payment_without_a_matching_cash_session_is_scheduled_for_retry()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        DateTime capturedAt = DateTime.UtcNow;
        PaymentCaptured payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            9m,
            "Cash",
            "DineIn",
            "Pos",
            "REF-456",
            capturedAt
        );

        await fixture.DeliverAsync(Guid.NewGuid(), payment);

        Assert.Equal(1, fixture.Channel.RetryPublications);
        Assert.Equal(1, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        Assert.Empty(await fixture.GetMovementsAsync());
    }

    [Fact]
    public void Captured_payment_maps_totals_references_and_sale_group()
    {
        Guid messageId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid registerSessionId = Guid.NewGuid();
        Guid saleGroupId = Guid.NewGuid();
        DateTime capturedAt = new(2026, 10, 20, 13, 45, 0, DateTimeKind.Utc);
        PaymentCaptured payment = new(
            paymentId,
            orderId,
            restaurantId,
            18m,
            "Cash",
            "DineIn",
            "Pos",
            "REF-123",
            capturedAt,
            CashRegisterSessionId: registerSessionId,
            TipAmount: 2m,
            TenderedAmount: 25m,
            ChangeAmount: 5m,
            SaleGroupId: saleGroupId
        );
        BasicDeliverEventArgs args = Delivery(
            messageId.ToString(),
            typeof(PaymentCaptured).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(payment)
        );

        PaymentProjection projection = InvokeReadPayment(messageId, args);

        Assert.Equal(messageId, projection.MessageId);
        Assert.Equal(paymentId, projection.PaymentId);
        Assert.Equal(orderId, projection.OrderId);
        Assert.Equal(restaurantId, projection.RestaurantId);
        Assert.Equal(20m, projection.Amount);
        Assert.Equal("Cash", projection.Method);
        Assert.Equal("REF-123", projection.Reference);
        Assert.Equal(capturedAt, projection.OccurredAtUtc);
        Assert.Equal(registerSessionId, projection.CashRegisterSessionId);
        Assert.Equal(25m, projection.TenderedAmount);
        Assert.Equal(5m, projection.ChangeAmount);
        Assert.Equal(saleGroupId, projection.SaleId);
    }

    [Fact]
    public void Captured_payment_uses_transaction_id_when_sale_group_is_absent()
    {
        Guid transactionId = Guid.NewGuid();
        PaymentCaptured payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            7m,
            "Card",
            "DineIn",
            "Pos",
            "",
            DateTime.UtcNow,
            PaymentTransactionId: transactionId
        );

        PaymentProjection projection = InvokeReadPayment(
            Guid.NewGuid(),
            Delivery(
                Guid.NewGuid().ToString(),
                typeof(PaymentCaptured).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(payment)
            )
        );

        Assert.Equal(transactionId, projection.SaleId);
        Assert.Equal(7m, projection.Amount);
    }

    [Theory]
    [InlineData("invalid-id", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "null")]
    public async Task Invalid_payment_deliveries_are_dead_lettered(
        string messageId,
        string eventType,
        string body
    )
    {
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ChannelProxy proxy = (ChannelProxy)(object)channel;
        CashPaymentProjectionWorker worker = new(
            null!,
            Options.Create(new RabbitMqOptions()),
            NullLogger<CashPaymentProjectionWorker>.Instance
        );
        typeof(CashPaymentProjectionWorker)
            .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(worker, channel);
        BasicDeliverEventArgs args = Delivery(
            messageId == "{guid}" ? Guid.NewGuid().ToString() : messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );
        MethodInfo handle = typeof(CashPaymentProjectionWorker).GetMethod(
            "Handle",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await (Task)handle.Invoke(worker, [channel, args])!;

        Assert.Equal(0, proxy.Acknowledgements);
        Assert.Equal(1, proxy.Rejections);
    }

    private static PaymentProjection InvokeReadPayment(
        Guid messageId,
        BasicDeliverEventArgs args
    ) =>
        (PaymentProjection)
            typeof(CashPaymentProjectionWorker)
                .GetMethod("ReadPayment", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [messageId, args])!;

    private static BasicDeliverEventArgs Delivery(
        string messageId,
        string eventType,
        byte[] body
    ) =>
        new(
            "consumer",
            1,
            false,
            "exchange",
            "routing-key",
            new BasicProperties { MessageId = messageId, Type = eventType },
            body,
            TestContext.Current.CancellationToken
        );

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public int RetryPublications { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                Rejections++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                RetryPublications++;
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly CashPaymentProjectionWorker worker;
        private readonly IChannel channel;
        private readonly CapturingLogger logger;

        private WorkerFixture(
            ServiceProvider services,
            CashPaymentProjectionWorker worker,
            IChannel channel,
            ChannelProxy proxy,
            CapturingLogger logger
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            Channel = proxy;
            this.logger = logger;
        }

        public ChannelProxy Channel { get; }
        public Exception? LastException => logger.LastException;

        public static async Task<WorkerFixture> CreateAsync()
        {
            string databaseName = Guid.NewGuid().ToString();
            InMemoryDatabaseRoot databaseRoot = new();
            ServiceProvider services = new ServiceCollection()
                .AddDbContext<CashRegisterDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName, databaseRoot)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
                        )
                )
                .AddSingleton(TimeProvider.System)
                .AddScoped<ICashRegisterStore, CashRegisterStore>()
                .AddScoped<CashPaymentProjectionService>()
                .BuildServiceProvider();
            await using (AsyncServiceScope scope = services.CreateAsyncScope())
            {
                await scope
                    .ServiceProvider.GetRequiredService<CashRegisterDbContext>()
                    .Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            }

            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            CapturingLogger logger = new();
            CashPaymentProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                logger
            );
            typeof(CashPaymentProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return new WorkerFixture(
                services,
                worker,
                channel,
                (ChannelProxy)(object)channel,
                logger
            );
        }

        public async Task AddOpenSessionAsync(Guid restaurantId, Guid sessionId, DateTime openedAt)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<CashRegisterDbContext>()
                .Sessions.Add(
                    new CashRegisterSession
                    {
                        Id = sessionId,
                        RestaurantId = restaurantId,
                        BusinessDate = DateOnly.FromDateTime(openedAt),
                        Status = CashRegisterStatus.Open,
                        OpeningFloat = 50m,
                        OpenedAtUtc = openedAt.AddHours(-1),
                        OpenedByName = "Cajera",
                    }
                );
            await scope
                .ServiceProvider.GetRequiredService<CashRegisterDbContext>()
                .SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async Task DeliverAsync(Guid messageId, PaymentCaptured payment)
        {
            BasicDeliverEventArgs args = Delivery(
                messageId.ToString(),
                typeof(PaymentCaptured).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(payment)
            );
            MethodInfo handle = typeof(CashPaymentProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async Task<List<CashMovement>> GetMovementsAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<CashRegisterDbContext>()
                .Movements.AsNoTracking()
                .ToListAsync(TestContext.Current.CancellationToken);
        }

        public async Task<bool> WasMessageProcessedAsync(Guid messageId)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<CashRegisterDbContext>()
                .InboxMessages.AnyAsync(
                    message => message.Id == messageId,
                    TestContext.Current.CancellationToken
                );
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private sealed class CapturingLogger : ILogger<CashPaymentProjectionWorker>
    {
        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => LastException = exception;
    }
}
