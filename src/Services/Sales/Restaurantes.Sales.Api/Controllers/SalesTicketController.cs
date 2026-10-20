using Microsoft.AspNetCore.Mvc;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Responses;

namespace Restaurantes.Sales.Api.Controllers;

[ApiController, Route("api/sales")]
public sealed class SalesTicketController(SaleTicketQueryService ticketQuery) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleResponse>> Find(Guid id, CancellationToken cancellationToken)
    {
        SaleResponse? sale = await ticketQuery.FindAsync(id, cancellationToken);
        return sale is null ? NotFound() : Ok(sale);
    }
}
