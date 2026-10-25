using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Contracts.Requests;
using Restaurantes.Dining.Domain;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningIntegrationPaymentTests
{
    [Fact]
    public async Task Checkout_applies_discount_captures_tip_and_records_change()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy storeProxy = GetProxy(store);
        DiningSessionOrder order = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = Guid.NewGuid(),
            Amount = 100m,
        };
        DiningSession session = new()
        {
            Id = order.DiningSessionId,
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            DiscountPercentage = 10m,
            Orders = [order],
        };
        storeProxy.Session = session;
        IDiningPayments payments = DispatchProxy.Create<IDiningPayments, DiningPaymentsProxy>();
        DiningPaymentsProxy paymentsProxy = (DiningPaymentsProxy)(object)payments;
        IDiningAuthorization authorization = DispatchProxy.Create<
            IDiningAuthorization,
            DiningAuthorizationProxy
        >();
        DiningService service = new(store, payments, null!, authorization, TimeProvider.System);
        ClaimsPrincipal principal = new(new ClaimsIdentity());
        Guid idempotencyKey = Guid.NewGuid();

        DiningResult result = await service.Checkout(
            session.Id,
            new CheckoutSessionRequest
            {
                IdempotencyKey = idempotencyKey,
                Method = "Cash",
                TipAmount = 10m,
                AmountReceived = 120m,
            },
            principal,
            new DiningRequest(principal, null, "Bearer token"),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
        Assert.Equal(90m, Assert.Single(paymentsProxy.AdjustedAmounts));
        DiningPaymentRequest captured = Assert.Single(paymentsProxy.CapturedPayments);
        Assert.Equal(90m, captured.Amount);
        Assert.Equal(10m, captured.TipAmount);
        Assert.Equal(20m, captured.TenderedAmount - captured.Amount - captured.TipAmount);
        Assert.Equal(idempotencyKey, session.CheckoutIdempotencyKey);
        Assert.Equal("Paid", order.PaymentStatus);
        Assert.True(storeProxy.Transaction.IsCommitted);
    }

    [Fact]
    public async Task Checkout_validates_idempotency_session_and_payment_permission()
    {
        (DiningService service, DiningStoreProxy store, _, ClaimsPrincipal principal) =
            CreateCheckoutHarness(null, []);
        DiningResult emptyKey = await service.Checkout(
            Guid.NewGuid(),
            new CheckoutSessionRequest(),
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.BadRequest, emptyKey.Outcome);
        Assert.False(store.Transaction.IsCommitted);

        (service, store, _, principal) = CreateCheckoutHarness(
            null,
            [DiningPermission.PaymentsCapture]
        );
        DiningResult missing = await service.Checkout(
            Guid.NewGuid(),
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid() },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.NotFound, missing.Outcome);

        DiningSession session = SessionWithOrder();
        (service, _, _, principal) = CreateCheckoutHarness(session, []);
        DiningResult forbidden = await service.Checkout(
            session.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid() },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Forbidden, forbidden.Outcome);
    }

    [Fact]
    public async Task Checkout_is_idempotent_for_open_or_closed_sessions_and_rejects_empty_sessions()
    {
        Guid key = Guid.NewGuid();
        DiningSession open = SessionWithOrder();
        open.CheckoutIdempotencyKey = key;
        (DiningService service, _, DiningPaymentsProxy payments, ClaimsPrincipal principal) =
            CreateCheckoutHarness(open, [DiningPermission.PaymentsCapture]);

        DiningResult repeatedOpen = await service.Checkout(
            open.Id,
            new CheckoutSessionRequest { IdempotencyKey = key },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, repeatedOpen.Outcome);
        Assert.Empty(payments.CapturedPayments);

        DiningSession closed = SessionWithOrder();
        closed.Status = "Closed";
        closed.CheckoutIdempotencyKey = key;
        (service, _, payments, principal) = CreateCheckoutHarness(
            closed,
            [DiningPermission.PaymentsCapture]
        );
        DiningResult repeatedClosed = await service.Checkout(
            closed.Id,
            new CheckoutSessionRequest { IdempotencyKey = key },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        DiningResult differentKey = await service.Checkout(
            closed.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid() },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, repeatedClosed.Outcome);
        Assert.Equal(DiningOutcome.Conflict, differentKey.Outcome);
        Assert.Empty(payments.CapturedPayments);

        DiningSession empty = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
        };
        (service, _, _, principal) = CreateCheckoutHarness(
            empty,
            [DiningPermission.PaymentsCapture]
        );
        DiningResult noOrders = await service.Checkout(
            empty.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid() },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, noOrders.Outcome);
    }

    [Fact]
    public async Task Checkout_rejects_invalid_allocations_and_received_amounts()
    {
        DiningSession session = SessionWithOrder();
        Guid orderId = session.Orders.Single().OrderId;
        (DiningService service, _, _, ClaimsPrincipal principal) = CreateCheckoutHarness(
            session,
            [DiningPermission.PaymentsCapture]
        );
        CheckoutSessionRequest[] invalidRequests =
        [
            new()
            {
                IdempotencyKey = Guid.NewGuid(),
                Allocations = [new CheckoutOrderAllocation(Guid.NewGuid(), 10m)],
            },
            new()
            {
                IdempotencyKey = Guid.NewGuid(),
                Allocations = [new CheckoutOrderAllocation(orderId, 101m)],
            },
            new()
            {
                IdempotencyKey = Guid.NewGuid(),
                Allocations =
                [
                    new CheckoutOrderAllocation(orderId, 40m),
                    new CheckoutOrderAllocation(orderId, 40m),
                ],
            },
            new()
            {
                IdempotencyKey = Guid.NewGuid(),
                Allocations = [new CheckoutOrderAllocation(orderId, 0m)],
            },
            new() { IdempotencyKey = Guid.NewGuid(), AmountReceived = 9.999m },
            new()
            {
                IdempotencyKey = Guid.NewGuid(),
                Method = "Transfer",
                AmountReceived = 101m,
            },
        ];

        foreach (CheckoutSessionRequest request in invalidRequests)
        {
            DiningResult result = await service.Checkout(
                session.Id,
                request,
                principal,
                new DiningRequest(principal, null, null),
                CancellationToken.None
            );
            Assert.Equal(DiningOutcome.BadRequest, result.Outcome);
        }
    }

    [Fact]
    public async Task Checkout_allocates_partial_payments_and_tips_proportionally()
    {
        DiningSession session = SessionWithOrder();
        DiningSessionOrder first = session.Orders.Single();
        first.Amount = 60m;
        DiningSessionOrder second = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = session.Id,
            Amount = 40m,
            OrderStatus = "Delivered",
        };
        session.Orders.Add(second);
        (DiningService service, _, DiningPaymentsProxy payments, ClaimsPrincipal principal) =
            CreateCheckoutHarness(session, [DiningPermission.PaymentsCapture]);

        DiningResult result = await service.Checkout(
            session.Id,
            new CheckoutSessionRequest
            {
                IdempotencyKey = Guid.NewGuid(),
                Method = "Cash",
                TipAmount = 5m,
                AmountReceived = 55m,
                Allocations =
                [
                    new CheckoutOrderAllocation(first.OrderId, 30m),
                    new CheckoutOrderAllocation(second.OrderId, 20m),
                ],
            },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
        Assert.Equal([30m, 20m], payments.CapturedPayments.Select(payment => payment.Amount));
        Assert.Equal([3m, 2m], payments.CapturedPayments.Select(payment => payment.TipAmount));
        Assert.Equal(30m, first.AmountPaid);
        Assert.Equal(20m, second.AmountPaid);
        Assert.Equal("PartiallyPaid", first.PaymentStatus);
        Assert.Equal("PartiallyPaid", second.PaymentStatus);
    }

    [Fact]
    public async Task Card_only_permission_limits_checkout_to_the_commanders_own_orders()
    {
        Guid userId = Guid.NewGuid();
        DiningSession session = SessionWithOrder();
        DiningSessionOrder ownOrder = session.Orders.Single();
        ownOrder.Source = "Comandero";
        ownOrder.CreatedByUserId = userId;
        DiningSessionOrder otherOrder = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = session.Id,
            Amount = 25m,
            OrderStatus = "Delivered",
            Source = "Pos",
        };
        session.Orders.Add(otherOrder);
        (DiningService service, _, DiningPaymentsProxy payments, ClaimsPrincipal principal) =
            CreateCheckoutHarness(session, [DiningPermission.PaymentsCaptureCard]);
        principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")
        );

        DiningResult result = await service.Checkout(
            session.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid(), Method = "Card" },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
        Assert.Single(payments.CapturedPayments);
        Assert.Equal(ownOrder.OrderId, payments.CapturedOrderIds.Single());
        Assert.Equal(100m, ownOrder.AmountPaid);
        Assert.Equal(0m, otherOrder.AmountPaid);
    }

    [Fact]
    public async Task Checkout_returns_conflict_when_amount_adjustment_or_payment_capture_fails()
    {
        DiningSession discounted = SessionWithOrder();
        discounted.DiscountPercentage = 10m;
        (DiningService service, _, DiningPaymentsProxy payments, ClaimsPrincipal principal) =
            CreateCheckoutHarness(discounted, [DiningPermission.PaymentsCapture]);
        payments.AdjustmentSucceeded = false;

        DiningResult adjustmentFailure = await service.Checkout(
            discounted.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid(), Method = "Cash" },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Conflict, adjustmentFailure.Outcome);
        Assert.Empty(payments.CapturedPayments);

        DiningSession fullPrice = SessionWithOrder();
        (service, _, payments, principal) = CreateCheckoutHarness(
            fullPrice,
            [DiningPermission.PaymentsCapture]
        );
        payments.CaptureSucceeded = false;
        DiningResult captureFailure = await service.Checkout(
            fullPrice.Id,
            new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid(), Method = "Card" },
            principal,
            new DiningRequest(principal, null, null),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Conflict, captureFailure.Outcome);
        Assert.Null(fullPrice.CheckoutIdempotencyKey);
        Assert.False(fullPrice.Orders.Single().AmountPaid > 0);
    }

    [Fact]
    public async Task GetBill_enforces_read_permission_and_reports_discount_payment_and_release_state()
    {
        DiningSession session = SessionWithOrder();
        session.DiscountPercentage = 10m;
        DiningSessionOrder order = session.Orders.Single();
        order.AmountPaid = 50m;
        order.TipPaid = 5m;
        (DiningService service, DiningStoreProxy store, _, ClaimsPrincipal principal) =
            CreateCheckoutHarness(session, []);
        DiningResult forbidden = await service.GetBill(
            session.Id,
            principal,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Forbidden, forbidden.Outcome);

        (service, store, _, principal) = CreateCheckoutHarness(
            session,
            [DiningPermission.TablesRead]
        );
        DiningResult result = await service.GetBill(session.Id, principal, CancellationToken.None);

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
        store.Session = null;
        DiningResult missing = await service.GetBill(
            Guid.NewGuid(),
            principal,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.NotFound, missing.Outcome);
        JsonElement bill = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(95m, bill.GetProperty("total").GetDecimal());
        Assert.Equal(10m, bill.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(5m, bill.GetProperty("tipTotal").GetDecimal());
        Assert.Equal(40m, bill.GetProperty("outstanding").GetDecimal());
        Assert.True(bill.GetProperty("canCheckout").GetBoolean());
        Assert.False(bill.GetProperty("canRelease").GetBoolean());
    }

    [Fact]
    public async Task Captured_payment_updates_projected_order_amount_tip_and_status()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid sessionId = Guid.NewGuid();
        DiningSessionOrder order = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = sessionId,
            Amount = 100m,
            AmountPaid = 20m,
            TipPaid = 2m,
        };
        proxy.Order = order;
        proxy.Session = new DiningSession
        {
            Id = sessionId,
            DiscountPercentage = 10m,
            Orders = [order],
        };
        PaymentCaptured payment = Captured(order.OrderId, amountPaid: 50m, tipAmount: 5m);

        await ProcessAsync(store, payment);

        Assert.Equal(50m, order.AmountPaid);
        Assert.Equal(7m, order.TipPaid);
        Assert.Equal("PartiallyPaid", order.PaymentStatus);
        Assert.Equal(payment.CapturedAtUtc, order.UpdatedAtUtc);
    }

    [Fact]
    public async Task Captured_payment_uses_cumulative_values_when_present()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        DiningSessionOrder order = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = Guid.NewGuid(),
            Amount = 100m,
            TipPaid = 2m,
        };
        proxy.Order = order;
        proxy.Session = new DiningSession { Orders = [order] };
        PaymentCaptured payment = Captured(
            order.OrderId,
            amountPaid: 100m,
            tipAmount: 5m,
            tipAmountPaid: 8m
        );

        await ProcessAsync(store, payment);

        Assert.Equal(100m, order.AmountPaid);
        Assert.Equal(8m, order.TipPaid);
        Assert.Equal("Paid", order.PaymentStatus);
    }

    [Fact]
    public async Task Captured_payment_before_order_creation_is_saved_as_pending()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        PaymentCaptured payment = Captured(Guid.NewGuid(), amountPaid: 0m, tipAmount: 3m);

        await ProcessAsync(store, payment);

        Assert.NotNull(proxy.PendingPayment);
        Assert.Equal(payment.OrderId, proxy.PendingPayment.OrderId);
        Assert.Equal("Paid", proxy.PendingPayment.Status);
        Assert.Equal(0m, proxy.PendingPayment.AmountPaid);
        Assert.Equal(3m, proxy.PendingPayment.TipPaid);

        PaymentCaptured additionalCapture = payment with
        {
            AmountPaid = 18m,
            TipAmount = 2m,
            CapturedAtUtc = payment.CapturedAtUtc.AddMinutes(1),
        };
        await ProcessAsync(store, additionalCapture);
        Assert.Equal(18m, proxy.PendingPayment.AmountPaid);
        Assert.Equal(5m, proxy.PendingPayment.TipPaid);
        Assert.Equal(additionalCapture.CapturedAtUtc, proxy.PendingPayment.UpdatedAtUtc);

        PaymentRefunded refund = new(
            Guid.NewGuid(),
            payment.OrderId,
            Guid.NewGuid(),
            18m,
            "Card",
            "DineIn",
            "TPV",
            "Test",
            additionalCapture.CapturedAtUtc.AddMinutes(1)
        );
        await ProcessAsync(store, refund);
        Assert.Equal("Refunded", proxy.PendingPayment.Status);
        Assert.Equal(0m, proxy.PendingPayment.AmountPaid);
        Assert.Equal(0m, proxy.PendingPayment.TipPaid);
        Assert.Equal(refund.RefundedAtUtc, proxy.PendingPayment.UpdatedAtUtc);
    }

    [Fact]
    public async Task Refund_resets_projected_order_payment_values()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        DiningSessionOrder order = new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = Guid.NewGuid(),
            Amount = 25m,
            AmountPaid = 25m,
            TipPaid = 3m,
        };
        proxy.Order = order;
        PaymentRefunded refund = new(
            Guid.NewGuid(),
            order.OrderId,
            Guid.NewGuid(),
            25m,
            "Card",
            "DineIn",
            "TPV",
            "Test",
            DateTime.UtcNow
        );

        await ProcessAsync(store, refund);

        Assert.Equal(0m, order.AmountPaid);
        Assert.Equal(0m, order.TipPaid);
        Assert.Equal("Refunded", order.PaymentStatus);
        Assert.Equal(refund.RefundedAtUtc, order.UpdatedAtUtc);
    }

    [Fact]
    public async Task Takeaway_payment_before_order_creation_is_applied_to_the_pending_payment_projection()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        PaymentCaptured payment = Captured(
            Guid.NewGuid(),
            amountPaid: 25m,
            tipAmountPaid: 2m,
            serviceMode: "Takeaway"
        );

        await ProcessAsync(store, payment);

        Assert.Equal(payment.AmountPaid, proxy.PendingPayment?.AmountPaid);
        Assert.Equal(payment.TipAmountPaid, proxy.PendingPayment?.TipPaid);
        Assert.Equal("Paid", proxy.PendingPayment?.Status);
        Assert.Null(proxy.Order);
        Assert.True(proxy.MessageWasMarkedProcessed);
    }

    [Fact]
    public async Task ProcessAsync_skips_duplicates_and_marks_order_without_dining_session()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        proxy.HasProcessedMessage = true;
        await ProcessAsync(store, Created(Guid.NewGuid(), diningSessionId: null));
        Assert.False(proxy.MessageWasMarkedProcessed);

        proxy.HasProcessedMessage = false;
        await ProcessAsync(store, Created(Guid.NewGuid(), diningSessionId: null));
        Assert.True(proxy.MessageWasMarkedProcessed);
        Assert.Null(proxy.Order);
    }

    [Fact]
    public async Task Order_events_without_a_dining_session_are_marked_without_projecting_an_order()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateTime occurredAt = DateTime.UtcNow;

        await ProcessAsync(
            store,
            Submitted(orderId, sessionId, [], []) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            Delivered(orderId, sessionId, [], occurredAt) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            Ready(orderId, sessionId, [], []) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            PreparationStarted(orderId, sessionId) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            TicketReady(orderId, sessionId, []) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            Dispatched(orderId, sessionId, []) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            new OrderLineCancelled(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                sessionId,
                "Table",
                "",
                "DineIn",
                "Pos",
                "AtTable",
                Guid.NewGuid(),
                "HOT",
                "Customer request",
                "Ready",
                12m,
                2,
                occurredAt,
                occurredAt,
                occurredAt,
                [Line()],
                [Station("Dispatched")]
            ) with
            {
                DiningSessionId = null,
            }
        );
        await ProcessAsync(
            store,
            new OrderCancelled(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                sessionId,
                "Table",
                "",
                "DineIn",
                "Pos",
                "AtTable",
                "Reason",
                "User",
                3,
                occurredAt,
                occurredAt,
                occurredAt,
                [Line()],
                []
            ) with
            {
                DiningSessionId = null,
            }
        );

        Assert.Null(proxy.Order);
        Assert.True(proxy.MessageWasMarkedProcessed);
    }

    [Fact]
    public async Task Order_created_projects_order_and_applies_pending_payment()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime occurredAt = DateTime.UtcNow;
        proxy.Session = new DiningSession
        {
            Id = sessionId,
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            Status = "Open",
        };
        DiningPendingPayment pending = new()
        {
            OrderId = orderId,
            Status = "Paid",
            AmountPaid = 10m,
            TipPaid = 2m,
            UpdatedAtUtc = occurredAt,
        };
        proxy.PendingPayment = pending;

        await ProcessAsync(
            store,
            Created(orderId, sessionId, occurredAt, [Line(unitPrice: 20m, quantity: 1)]) with
            {
                RestaurantId = proxy.Session.RestaurantId,
                TableId = proxy.Session.TableId,
            }
        );

        Assert.Equal(orderId, proxy.Order!.OrderId);
        Assert.Equal(20m, proxy.Order.Amount);
        Assert.Equal(10m, proxy.Order.AmountPaid);
        Assert.Equal(2m, proxy.Order.TipPaid);
        Assert.Equal("PartiallyPaid", proxy.Order.PaymentStatus);
        Assert.Equal(occurredAt, proxy.Order.AddedAtUtc);
        Assert.Null(proxy.PendingPayment);

        Guid unpaidOrderId = Guid.NewGuid();
        proxy.Order = null;
        await ProcessAsync(
            store,
            Created(unpaidOrderId, sessionId, occurredAt, [Line(unitPrice: 7m)]) with
            {
                RestaurantId = proxy.Session.RestaurantId,
                TableId = proxy.Session.TableId,
            }
        );
        Assert.Equal(unpaidOrderId, proxy.Order!.OrderId);
        Assert.Equal(7m, proxy.Order.Amount);
        Assert.Equal(0m, proxy.Order.AmountPaid);
        Assert.Equal(0m, proxy.Order.TipPaid);
        Assert.Equal("Unpaid", proxy.Order.PaymentStatus);
    }

    [Fact]
    public async Task Order_created_ignores_existing_order_and_rejects_mismatched_session()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        proxy.Session = new DiningSession
        {
            Id = sessionId,
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
        };
        proxy.Order = new DiningSessionOrder { OrderId = orderId };

        await ProcessAsync(
            store,
            Created(orderId, sessionId) with
            {
                RestaurantId = proxy.Session.RestaurantId,
                TableId = proxy.Session.TableId,
            }
        );
        Assert.Equal(orderId, proxy.Order.OrderId);

        proxy.Order = null;
        OrderCreated mismatched = Created(orderId, sessionId) with
        {
            RestaurantId = Guid.NewGuid(),
            TableId = proxy.Session.TableId,
        };
        await Assert.ThrowsAsync<JsonException>(() => ProcessAsync(store, mismatched));

        proxy.Session = null;
        await Assert.ThrowsAsync<JsonException>(() =>
            ProcessAsync(store, Created(orderId, sessionId))
        );

        proxy.Session = new DiningSession
        {
            Id = sessionId,
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            Status = "Open",
        };
        await Assert.ThrowsAsync<JsonException>(() =>
            ProcessAsync(
                store,
                Created(orderId, sessionId) with
                {
                    RestaurantId = proxy.Session.RestaurantId,
                    TableId = Guid.NewGuid(),
                }
            )
        );

        proxy.Session.Status = "Closed";
        await Assert.ThrowsAsync<JsonException>(() =>
            ProcessAsync(
                store,
                Created(orderId, sessionId) with
                {
                    RestaurantId = proxy.Session.RestaurantId,
                    TableId = proxy.Session.TableId,
                }
            )
        );
    }

    [Fact]
    public async Task Order_status_events_update_projection_and_ignore_cancelled_lines()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DiningSessionOrder order = new()
        {
            OrderId = orderId,
            DiningSessionId = sessionId,
            Amount = 99m,
        };
        proxy.Order = order;
        OrderLineSnapshot[] lines =
        [
            Line(unitPrice: 10m),
            Line(unitPrice: 50m, status: "Cancelled"),
        ];

        await ProcessAsync(store, Submitted(orderId, sessionId, lines, []));
        Assert.Equal("Submitted", order.OrderStatus);
        Assert.Equal(10m, order.Amount);

        DateTime deliveredAt = DateTime.UtcNow;
        await ProcessAsync(store, Delivered(orderId, sessionId, lines, deliveredAt));
        Assert.Equal("Delivered", order.OrderStatus);
        Assert.Equal(10m, order.Amount);
        Assert.Equal(deliveredAt, order.UpdatedAtUtc);

        await ProcessAsync(store, Ready(orderId, sessionId, lines, []));
        Assert.Equal("Ready", order.OrderStatus);
        await ProcessAsync(store, Ready(orderId, sessionId, lines, [Station("Dispatched")]));
        Assert.Equal("Delivered", order.OrderStatus);
    }

    [Fact]
    public async Task Kitchen_ticket_events_follow_station_state_and_timestamp_fallbacks()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DiningSessionOrder order = new() { OrderId = orderId, DiningSessionId = sessionId };
        proxy.Order = order;
        DateTime stationReadyAt = DateTime.UtcNow.AddMinutes(-1);
        OrderKitchenStationSnapshot[] stations = [Station("Ready", readyAtUtc: stationReadyAt)];

        await ProcessAsync(store, PreparationStarted(orderId, sessionId));
        Assert.Equal("InPreparation", order.OrderStatus);
        await ProcessAsync(store, TicketReady(orderId, sessionId, stations));
        Assert.Equal("InPreparation", order.OrderStatus);
        Assert.Equal(stationReadyAt, order.UpdatedAtUtc);
        await ProcessAsync(
            store,
            TicketReady(orderId, sessionId, stations, readyAtUtc: stationReadyAt)
        );
        Assert.Equal("Ready", order.OrderStatus);

        await ProcessAsync(store, TicketReady(orderId, sessionId, [Station("Cancelled")]));
        Assert.Equal("InPreparation", order.OrderStatus);
        await ProcessAsync(store, Dispatched(orderId, sessionId, [Station("Dispatched")]));
        Assert.Equal("Delivered", order.OrderStatus);
        await ProcessAsync(store, Dispatched(orderId, sessionId, [Station("Ready")]));
        Assert.Equal("Ready", order.OrderStatus);
    }

    [Fact]
    public async Task Order_cancellation_events_update_total_and_status()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DiningSessionOrder order = new() { OrderId = orderId, DiningSessionId = sessionId };
        proxy.Order = order;

        await ProcessAsync(
            store,
            new OrderLineCancelled(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                sessionId,
                "Table",
                "",
                "DineIn",
                "Pos",
                "AtTable",
                Guid.NewGuid(),
                "HOT",
                "Customer request",
                "Ready",
                12m,
                2,
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow,
                [Line()],
                [Station("Dispatched")]
            )
        );
        Assert.Equal("Delivered", order.OrderStatus);
        Assert.Equal(12m, order.Amount);

        DateTime cancelledAt = DateTime.UtcNow;
        await ProcessAsync(
            store,
            new OrderCancelled(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                sessionId,
                "Table",
                "",
                "DineIn",
                "Pos",
                "AtTable",
                "Reason",
                "User",
                3,
                DateTime.UtcNow,
                DateTime.UtcNow,
                cancelledAt,
                [Line()],
                []
            )
        );
        Assert.Equal("Cancelled", order.OrderStatus);
        Assert.Equal(0m, order.Amount);
        Assert.Equal(cancelledAt, order.UpdatedAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_rejects_unsupported_and_empty_event_payloads()
    {
        DiningIntegrationService service = new(CreateStore(), TimeProvider.System);
        await Assert.ThrowsAsync<JsonException>(() =>
            service.ProcessAsync(Guid.NewGuid(), "unsupported", ReadOnlyMemory<byte>.Empty)
        );
        await Assert.ThrowsAsync<JsonException>(() =>
            service.ProcessAsync(
                Guid.NewGuid(),
                typeof(OrderCancelled).FullName!,
                ReadOnlyMemory<byte>.Empty
            )
        );
        Type[] eventTypes =
        [
            typeof(OrderCreated),
            typeof(OrderSubmitted),
            typeof(KitchenTicketPreparationStarted),
            typeof(OrderDelivered),
            typeof(OrderReady),
            typeof(KitchenTicketReady),
            typeof(KitchenTicketDispatched),
            typeof(OrderLineCancelled),
            typeof(OrderCancelled),
            typeof(PaymentCaptured),
            typeof(PaymentRefunded),
        ];
        ReadOnlyMemory<byte> nullPayload = "null"u8.ToArray();
        foreach (Type eventType in eventTypes)
        {
            await Assert.ThrowsAsync<JsonException>(() =>
                service.ProcessAsync(Guid.NewGuid(), eventType.FullName!, nullPayload)
            );
        }
    }

    [Fact]
    public async Task Status_events_for_missing_orders_are_not_acknowledged()
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy proxy = GetProxy(store);
        proxy.Order = null;
        Guid orderId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        object[] events =
        [
            Submitted(orderId, sessionId, [Line()], []),
            Delivered(orderId, sessionId, [Line()], now),
            Ready(orderId, sessionId, [Line()], []),
            PreparationStarted(orderId, sessionId),
            TicketReady(orderId, sessionId, []),
            Dispatched(orderId, sessionId, []),
        ];
        DiningIntegrationService service = new(store, TimeProvider.System);

        foreach (object integrationEvent in events)
        {
            await Assert.ThrowsAsync<JsonException>(() =>
                service.ProcessAsync(
                    Guid.NewGuid(),
                    integrationEvent.GetType().FullName!,
                    JsonSerializer.SerializeToUtf8Bytes(integrationEvent)
                )
            );
        }

        Assert.False(proxy.MessageWasMarkedProcessed);
    }

    private static IDiningStore CreateStore()
    {
        return DispatchProxy.Create<IDiningStore, DiningStoreProxy>();
    }

    private static DiningStoreProxy GetProxy(IDiningStore store)
    {
        return (DiningStoreProxy)(object)store;
    }

    private static DiningSession SessionWithOrder()
    {
        Guid sessionId = Guid.NewGuid();
        return new DiningSession
        {
            Id = sessionId,
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            Orders =
            [
                new DiningSessionOrder
                {
                    OrderId = Guid.NewGuid(),
                    DiningSessionId = sessionId,
                    Amount = 100m,
                    OrderStatus = "Delivered",
                },
            ],
        };
    }

    private static (
        DiningService Service,
        DiningStoreProxy Store,
        DiningPaymentsProxy Payments,
        ClaimsPrincipal Principal
    ) CreateCheckoutHarness(DiningSession? session, IEnumerable<DiningPermission> permissions)
    {
        IDiningStore store = CreateStore();
        DiningStoreProxy storeProxy = GetProxy(store);
        storeProxy.Session = session;
        IDiningPayments payments = DispatchProxy.Create<IDiningPayments, DiningPaymentsProxy>();
        DiningPaymentsProxy paymentsProxy = (DiningPaymentsProxy)(object)payments;
        IDiningAuthorization authorization = DispatchProxy.Create<
            IDiningAuthorization,
            DiningAuthorizationProxy
        >();
        ((DiningAuthorizationProxy)(object)authorization).Permissions = permissions.ToHashSet();
        DiningService service = new(store, payments, null!, authorization, TimeProvider.System);
        ClaimsPrincipal principal = new(new ClaimsIdentity());
        return (service, storeProxy, paymentsProxy, principal);
    }

    private static PaymentCaptured Captured(
        Guid orderId,
        decimal amountPaid = 0m,
        decimal tipAmount = 0m,
        decimal tipAmountPaid = 0m,
        string serviceMode = "DineIn"
    )
    {
        return new PaymentCaptured(
            Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            amountPaid,
            "Card",
            serviceMode,
            "TPV",
            "external",
            DateTime.UtcNow,
            TipAmount: tipAmount,
            AmountPaid: amountPaid,
            TipAmountPaid: tipAmountPaid
        );
    }

    private static OrderCreated Created(
        Guid orderId,
        Guid? diningSessionId,
        DateTime? occurredAtUtc = null,
        IReadOnlyList<OrderLineSnapshot>? lines = null
    )
    {
        return new OrderCreated(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            diningSessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            1,
            occurredAtUtc ?? DateTime.UtcNow,
            lines ?? [Line()],
            []
        );
    }

    private static OrderLineSnapshot Line(
        decimal unitPrice = 10m,
        int quantity = 1,
        string status = "Active"
    )
    {
        return new OrderLineSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Product",
            unitPrice,
            quantity,
            "",
            null,
            "Category",
            "HOT",
            "Hot",
            status
        );
    }

    private static OrderKitchenStationSnapshot Station(string status, DateTime? readyAtUtc = null)
    {
        return new OrderKitchenStationSnapshot(
            "HOT",
            "Hot",
            status,
            false,
            0,
            null,
            readyAtUtc,
            null
        );
    }

    private static OrderSubmitted Submitted(
        Guid orderId,
        Guid sessionId,
        IReadOnlyList<OrderLineSnapshot> lines,
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    )
    {
        DateTime now = DateTime.UtcNow;
        return new OrderSubmitted(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            1,
            now,
            now,
            lines,
            stations
        );
    }

    private static OrderDelivered Delivered(
        Guid orderId,
        Guid sessionId,
        IReadOnlyList<OrderLineSnapshot> lines,
        DateTime deliveredAt
    )
    {
        DateTime now = DateTime.UtcNow;
        return new OrderDelivered(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            1,
            now,
            now,
            now,
            now,
            deliveredAt,
            lines,
            []
        );
    }

    private static OrderReady Ready(
        Guid orderId,
        Guid sessionId,
        IReadOnlyList<OrderLineSnapshot> lines,
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    )
    {
        DateTime now = DateTime.UtcNow;
        return new OrderReady(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            1,
            now,
            now,
            now,
            now,
            lines,
            stations
        );
    }

    private static KitchenTicketPreparationStarted PreparationStarted(Guid orderId, Guid sessionId)
    {
        DateTime now = DateTime.UtcNow;
        return new KitchenTicketPreparationStarted(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            "HOT",
            1,
            now,
            now,
            now,
            [Line()],
            []
        );
    }

    private static KitchenTicketReady TicketReady(
        Guid orderId,
        Guid sessionId,
        IReadOnlyList<OrderKitchenStationSnapshot> stations,
        DateTime? readyAtUtc = null
    )
    {
        DateTime now = DateTime.UtcNow;
        return new KitchenTicketReady(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            "HOT",
            1,
            now,
            now,
            now,
            readyAtUtc,
            [Line()],
            stations
        );
    }

    private static KitchenTicketDispatched Dispatched(
        Guid orderId,
        Guid sessionId,
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    )
    {
        DateTime now = DateTime.UtcNow;
        return new KitchenTicketDispatched(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            sessionId,
            "Table",
            "",
            "DineIn",
            "Pos",
            "AtTable",
            "HOT",
            1,
            now,
            now,
            now,
            now,
            [Line()],
            stations
        );
    }

    private static Task ProcessAsync<T>(IDiningStore store, T message)
    {
        DiningIntegrationService service = new(store, TimeProvider.System);
        return service.ProcessAsync(
            Guid.NewGuid(),
            typeof(T).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(message)
        );
    }

    public class DiningStoreProxy : DispatchProxy
    {
        public DiningSessionOrder? Order { get; set; }
        public DiningSession? Session { get; set; }
        public DiningTestTransaction Transaction { get; } = new();
        public DiningPendingPayment? PendingPayment { get; set; }
        public bool MessageWasMarkedProcessed { get; private set; }
        public bool HasProcessedMessage { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IDiningStore.HasProcessedMessageAsync) => Task.FromResult(
                    HasProcessedMessage
                ),
                nameof(IDiningStore.FindOrderAsync) => Task.FromResult(Order),
                nameof(IDiningStore.FindSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.LockSessionAsync) => Task.FromResult(Session),
                nameof(IDiningStore.LoadOrdersAsync) => Task.CompletedTask,
                nameof(IDiningStore.ReadSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.BeginTransactionAsync) => Task.FromResult<IDiningTransaction>(
                    Transaction
                ),
                nameof(IDiningStore.FindPendingPaymentAsync) => Task.FromResult(PendingPayment),
                nameof(IDiningStore.Add) => Add(args![0]!),
                nameof(IDiningStore.RemovePendingPayment) => RemovePendingPayment(args![0]!),
                nameof(IDiningStore.MarkMessageProcessed) => MarkMessageProcessed(),
                nameof(IDiningStore.SaveChangesAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }

        private object? Add(object entity)
        {
            if (entity is DiningPendingPayment payment)
            {
                PendingPayment = payment;
            }
            else
            {
                Order = Assert.IsType<DiningSessionOrder>(entity);
            }

            return null;
        }

        private object? RemovePendingPayment(object entity)
        {
            Assert.Same(PendingPayment, entity);
            PendingPayment = null;
            return null;
        }

        private object? MarkMessageProcessed()
        {
            MessageWasMarkedProcessed = true;
            return null;
        }
    }

    public class DiningPaymentsProxy : DispatchProxy
    {
        public List<decimal> AdjustedAmounts { get; } = [];
        public List<DiningPaymentRequest> CapturedPayments { get; } = [];
        public List<Guid> CapturedOrderIds { get; } = [];
        public bool AdjustmentSucceeded { get; set; } = true;
        public bool CaptureSucceeded { get; set; } = true;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDiningPayments.AdjustAmountAsync))
            {
                AdjustedAmounts.Add((decimal)args![2]!);
                return Task.FromResult(
                    new DiningPaymentResult(
                        AdjustmentSucceeded,
                        AdjustmentSucceeded ? 200 : 503,
                        "{}"
                    )
                );
            }

            if (targetMethod?.Name == nameof(IDiningPayments.CaptureAsync))
            {
                CapturedPayments.Add((DiningPaymentRequest)args![1]!);
                CapturedOrderIds.Add((Guid)args[0]!);
                DiningPaymentRequest request = (DiningPaymentRequest)args[1]!;
                return Task.FromResult(
                    new DiningPaymentResult(
                        CaptureSucceeded,
                        CaptureSucceeded ? 200 : 503,
                        "{}",
                        CaptureSucceeded ? request.Amount : 0,
                        CaptureSucceeded ? request.TipAmount : 0
                    )
                );
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class DiningAuthorizationProxy : DispatchProxy
    {
        public HashSet<DiningPermission>? Permissions { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == nameof(IDiningAuthorization.CanAccessRestaurant)
                ? Permissions is null || Permissions.Contains((DiningPermission)args![2]!)
                : throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public sealed class DiningTestTransaction : IDiningTransaction
    {
        public bool IsCommitted { get; private set; }

        public Task CommitAsync(CancellationToken ct)
        {
            IsCommitted = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
