using Restaurantes.CashRegister.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashPaymentProjectionWorkerShutdownTests
{
    [Fact]
    public async Task StopAsync_handles_a_worker_that_never_started()
    {
        CashPaymentProjectionWorker worker = new(null!, null!, null!);

        Task stopTask = worker.StopAsync(CancellationToken.None);
        await stopTask;

        Assert.True(stopTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void Sale_id_prefers_the_sale_group()
    {
        Guid saleGroupId = Guid.NewGuid();
        Restaurantes.Payments.Contracts.Events.PaymentCaptured payment = CreatePayment(
            paymentTransactionId: Guid.NewGuid(),
            saleGroupId: saleGroupId
        );

        Assert.Equal(saleGroupId, ResolveSaleId(payment));
    }

    [Fact]
    public void Sale_id_uses_the_transaction_when_the_group_is_empty()
    {
        Guid transactionId = Guid.NewGuid();
        Restaurantes.Payments.Contracts.Events.PaymentCaptured payment = CreatePayment(
            paymentTransactionId: transactionId,
            saleGroupId: Guid.Empty
        );

        Assert.Equal(transactionId, ResolveSaleId(payment));
    }

    [Fact]
    public void Sale_id_falls_back_to_the_payment_when_other_ids_are_missing_or_empty()
    {
        Guid paymentId = Guid.NewGuid();
        Restaurantes.Payments.Contracts.Events.PaymentCaptured payment = CreatePayment(
            paymentId,
            paymentTransactionId: Guid.Empty
        );

        Assert.Equal(paymentId, ResolveSaleId(payment));
    }

    private static Guid ResolveSaleId(
        Restaurantes.Payments.Contracts.Events.PaymentCaptured payment
    )
    {
        System.Reflection.MethodInfo method = typeof(CashPaymentProjectionWorker).GetMethod(
            "GetSaleId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        )!;
        return (Guid)method.Invoke(null, [payment])!;
    }

    private static Restaurantes.Payments.Contracts.Events.PaymentCaptured CreatePayment(
        Guid? paymentId = null,
        Guid? paymentTransactionId = null,
        Guid? saleGroupId = null
    ) =>
        new(
            paymentId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            10m,
            "Cash",
            "DineIn",
            "Pos",
            string.Empty,
            DateTime.UtcNow,
            PaymentTransactionId: paymentTransactionId,
            SaleGroupId: saleGroupId
        );
}
