using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;

namespace Restaurantes.RestaurantOperations.Api.Controllers;

[ApiController, Authorize]
[Route("api/restaurant-operations/companies")]
public sealed class CompaniesWriteController(CompanyCommandService commandService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompanyResponse>> Create(
        CreateCompanyRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.IsInRole("Admin"))
        {
            return Forbid();
        }

        CompanyResponse? company = await commandService.CreateAsync(request, cancellationToken);
        return company is null
            ? Conflict(new ProblemDetails { Title = "Ya existe una empresa con ese CIF/NIF." })
            : Accepted($"/api/restaurant-operations/companies/{company.Id}", company);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompanyResponse>> Update(
        Guid id,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.IsInRole("Admin"))
        {
            return Forbid();
        }

        if (!await commandService.ExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        CompanyResponse? company = await commandService.UpdateAsync(id, request, cancellationToken);
        return company is null
            ? Conflict(new ProblemDetails { Title = "Ya existe otra empresa con ese CIF/NIF." })
            : Accepted($"/api/restaurant-operations/companies/{company.Id}", company);
    }
}
