using Microsoft.AspNetCore.Mvc;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Responses;

namespace Restaurantes.Sales.Api.Controllers;

[ApiController, Route("api/sales")]
public sealed class SalesReadController(ISaleReadStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleResponse>>> List(
        Guid? restaurantId,
        DateOnly? date,
        CancellationToken cancellationToken,
        int offset = 0
    )
    {
        return Ok(await store.ListAsync(restaurantId, date, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleResponse>> Find(Guid id, CancellationToken cancellationToken)
    {
        SaleResponse? sale = await store.FindAsync(id, cancellationToken);
        return sale is null ? NotFound() : Ok(sale);
    }
}
