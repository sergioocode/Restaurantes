using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Payments.Api.Controllers;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentsReadControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Payment_status_query_enforces_the_cash_manager_restaurant_scope(
        bool authorized
    )
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Query query = new();
        ClaimsIdentity identity = new(
            [
                new Claim(
                    RestaurantClaimTypes.Permission,
                    RestaurantSecurityExtensions.ScopedPermission(
                        authorized ? restaurantId : Guid.NewGuid(),
                        RestaurantPermissions.CashRegisterManage
                    )
                ),
            ],
            "Test"
        );
        PaymentsReadController controller = new(query)
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
        ActionResult<IReadOnlyList<OrderPaymentStatusResponse>> result =
            await controller.OrderStatuses(
                restaurantId,
                new OrderPaymentStatusesRequest { OrderIds = [orderId] },
                TestContext.Current.CancellationToken
            );

        if (authorized)
        {
            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(restaurantId, query.RestaurantId);
            Assert.Equal(new[] { orderId }, query.OrderIds);
        }
        else
        {
            Assert.IsType<ForbidResult>(result.Result);
            Assert.Null(query.RestaurantId);
        }
    }

    private sealed class Query : IPaymentStatusQuery
    {
        public Guid? RestaurantId { get; private set; }
        public Guid[] OrderIds { get; private set; } = [];

        public Task<IReadOnlyList<OrderPaymentStatusResponse>> ListAsync(
            Guid restaurantId,
            Guid[] orderIds,
            CancellationToken ct
        )
        {
            RestaurantId = restaurantId;
            OrderIds = orderIds;
            return Task.FromResult<IReadOnlyList<OrderPaymentStatusResponse>>([]);
        }
    }
}
