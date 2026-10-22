using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Dining.Api.Controllers;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Contracts.Requests;
using Restaurantes.Dining.Domain;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningWriteControllerCoverageTests
{
    [Fact]
    public async Task Write_endpoints_map_empty_store_results_to_http_responses()
    {
        DiningWriteController controller = CreateController(allowQrNetwork: true);
        Guid id = Guid.NewGuid();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.IsType<BadRequestObjectResult>(
            await controller.CreateTable(Guid.Empty, new CreateTableRequest(), ct)
        );
        Assert.IsType<NotFoundObjectResult>(
            await controller.UpdateTable(id, new UpdateTableRequest(), ct)
        );
        Assert.IsType<NotFoundObjectResult>(await controller.DeleteTable(id, ct));
        Assert.IsType<NotFoundObjectResult>(
            await controller.OpenSession(id, new OpenSessionRequest(), ct)
        );
        Assert.IsType<NotFoundObjectResult>(await controller.RotateQr(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.OpenQrSession("qr-code", 2, ct));
        Assert.IsType<NotFoundObjectResult>(
            await controller.SetGuestCount(id, new SetGuestCountRequest(2), ct)
        );
        Assert.IsType<NotFoundObjectResult>(
            await controller.Checkout(
                id,
                new CheckoutSessionRequest { IdempotencyKey = Guid.NewGuid() },
                ct
            )
        );
        Assert.IsType<NotFoundObjectResult>(
            await controller.SetSessionDiscount(id, new SetSessionDiscountRequest(5), ct)
        );
        Assert.IsType<NotFoundObjectResult>(await controller.CloseSession(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.CancelSession(id, ct));
        Assert.IsType<BadRequestObjectResult>(
            await controller.CreateZone(Guid.Empty, new SaveZoneRequest(), ct)
        );
        Assert.IsType<ForbidResult>(
            await controller.UpdateZone(id, Guid.NewGuid(), new SaveZoneRequest(), ct)
        );
    }

    [Fact]
    public async Task Anonymous_qr_endpoints_reject_requests_from_unconfigured_networks()
    {
        DiningWriteController controller = CreateController(allowQrNetwork: false);

        ObjectResult denied = Assert.IsType<ObjectResult>(
            await controller.OpenQrSession("qr-code", null, TestContext.Current.CancellationToken)
        );

        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Contains(
            "Acceso QR no autorizado",
            Assert.IsType<ProblemDetails>(denied.Value).Title
        );
    }

    private static DiningWriteController CreateController(bool allowQrNetwork)
    {
        IDiningStore store = DispatchProxy.Create<IDiningStore, EmptyStoreProxy>();
        IDiningPayments payments = DispatchProxy.Create<IDiningPayments, UnusedPaymentsProxy>();
        IDiningCashRegister cashRegister = DispatchProxy.Create<
            IDiningCashRegister,
            UnusedCashRegisterProxy
        >();
        IDiningAuthorization authorization = DispatchProxy.Create<
            IDiningAuthorization,
            DenyAllAuthorizationProxy
        >();
        DiningService service = new(
            store,
            payments,
            cashRegister,
            authorization,
            TimeProvider.System
        );
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                allowQrNetwork
                    ? new Dictionary<string, string?>
                    {
                        ["QrAccess:AllowedClientNetworks:0"] = "127.0.0.1/32",
                    }
                    : new Dictionary<string, string?>()
            )
            .Build();
        DiningWriteController controller = new(service, configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Connection = { RemoteIpAddress = IPAddress.Loopback },
                    User = new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };
        return controller;
    }

    public class EmptyStoreProxy : DispatchProxy
    {
        private readonly IDiningTransaction transaction = new NoOpTransaction();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IDiningStore.BeginTransactionAsync) => Task.FromResult<IDiningTransaction>(
                    transaction
                ),
                nameof(IDiningStore.LockTableAsync) => Task.FromResult<RestaurantTable?>(null),
                nameof(IDiningStore.LockSessionAsync) => Task.FromResult<DiningSession?>(null),
                nameof(IDiningStore.LockQrTableAsync) => Task.FromResult<RestaurantTable?>(null),
                nameof(IDiningStore.FindSessionWithOrdersAsync) => Task.FromResult<DiningSession?>(
                    null
                ),
                nameof(IDiningStore.Add) => null,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public class UnusedPaymentsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("No payment is expected in this endpoint test.");
    }

    public class UnusedCashRegisterProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IDiningCashRegister.EnsureOpenAsync)
                ? Task.CompletedTask
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class DenyAllAuthorizationProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IDiningAuthorization.CanAccessRestaurant)
                ? false
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class NoOpTransaction : IDiningTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
