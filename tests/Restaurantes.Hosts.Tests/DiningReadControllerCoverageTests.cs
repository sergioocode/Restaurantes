using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Restaurantes.Dining.Api.Controllers;
using Restaurantes.Dining.Application;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningReadControllerCoverageTests
{
    [Fact]
    public async Task Read_endpoints_map_denied_access_and_missing_sessions_to_http_results()
    {
        DiningReadController controller = CreateController(allowQrNetwork: true);
        Guid id = Guid.NewGuid();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.IsType<ForbidResult>(await controller.ListTables(id, ct));
        Assert.IsType<ForbidResult>(await controller.ListZones(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.ActiveSession(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.GetSession(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.GetBill(id, ct));
        Assert.IsType<NotFoundObjectResult>(await controller.PreviewQr("  qr-01 ", ct));
        Assert.IsType<ConflictObjectResult>(
            await controller.ValidateSession(id, Guid.NewGuid(), Guid.NewGuid(), null, null, ct)
        );
    }

    [Fact]
    public async Task Anonymous_qr_read_endpoints_reject_requests_from_unconfigured_networks()
    {
        DiningReadController controller = CreateController(allowQrNetwork: false);
        Guid id = Guid.NewGuid();

        ObjectResult qrDenied = Assert.IsType<ObjectResult>(
            await controller.PreviewQr("qr-01", TestContext.Current.CancellationToken)
        );
        ObjectResult validationDenied = Assert.IsType<ObjectResult>(
            await controller.ValidateSession(
                id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "CustomerQr",
                "DineIn",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(StatusCodes.Status403Forbidden, qrDenied.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, validationDenied.StatusCode);
        Assert.Contains(
            "Acceso QR no autorizado",
            Assert.IsType<ProblemDetails>(qrDenied.Value).Title
        );
        Assert.Contains(
            "Acceso QR no autorizado",
            Assert.IsType<ProblemDetails>(validationDenied.Value).Title
        );
    }

    private static DiningReadController CreateController(bool allowQrNetwork)
    {
        IDiningStore store = DispatchProxy.Create<IDiningStore, EmptyReadStoreProxy>();
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

        return new DiningReadController(service, configuration)
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
    }

    public class EmptyReadStoreProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IDiningStore.ReadActiveSessionWithOrdersAsync) =>
                    Task.FromResult<Restaurantes.Dining.Domain.DiningSession?>(null),
                nameof(IDiningStore.ReadSessionWithOrdersAsync) =>
                    Task.FromResult<Restaurantes.Dining.Domain.DiningSession?>(null),
                nameof(IDiningStore.ReadValidatedSessionAsync) =>
                    Task.FromResult<Restaurantes.Dining.Domain.DiningSession?>(null),
                nameof(IDiningStore.ReadQrTableAsync) =>
                    Task.FromResult<Restaurantes.Dining.Domain.RestaurantTable?>(null),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public class UnusedPaymentsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("No payment is expected in a read endpoint.");
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
}
