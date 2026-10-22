using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Api.Controllers;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.RestaurantOperations.Domain;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class CompaniesWriteControllerTests
{
    [Fact]
    public async Task Admin_creates_company_and_returns_accepted_location_with_its_version()
    {
        CompanyWriteStore store = new();
        CompaniesWriteController controller = CreateController(store, isAdmin: true);

        ActionResult<CompanyResponse> result = await controller.Create(
            new CreateCompanyRequest
            {
                LegalName = "Restaurantes Norte",
                TaxId = " b 123 ",
                FiscalAddress = "Calle Mayor 1",
            },
            CancellationToken.None
        );

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
        CompanyResponse response = Assert.IsType<CompanyResponse>(accepted.Value);
        Assert.Equal($"/api/restaurant-operations/companies/{response.Id}", accepted.Location);
        Assert.Equal("B123", response.TaxId);
        Assert.Equal(response.Id, store.SavedEvent?.CompanyId);
        Assert.Equal(1, store.SavedEvent?.Version);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Existing_tax_id_returns_conflict_without_saving_another_company()
    {
        Company existing = Company.Create("Otra", "B123", "Calle 2", null, null, DateTime.UtcNow);
        CompanyWriteStore store = new(existing);
        CompaniesWriteController controller = CreateController(store, isAdmin: true);

        ActionResult<CompanyResponse> result = await controller.Create(
            new CreateCompanyRequest
            {
                LegalName = "Restaurantes Norte",
                TaxId = " b 123 ",
                FiscalAddress = "Calle Mayor 1",
            },
            CancellationToken.None
        );

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Ya existe una empresa con ese CIF/NIF.", problem.Title);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Admin_updates_company_and_returns_the_new_version()
    {
        Company company = Company.Create(
            "Original",
            "B123",
            "Calle 1",
            null,
            null,
            DateTime.UtcNow
        );
        CompanyWriteStore store = new(company);
        CompaniesWriteController controller = CreateController(store, isAdmin: true);

        ActionResult<CompanyResponse> result = await controller.Update(
            company.Id,
            new UpdateCompanyRequest
            {
                LegalName = "Restaurantes Norte",
                TaxId = " b 123 ",
                FiscalAddress = "Calle Mayor 1",
            },
            CancellationToken.None
        );

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
        CompanyResponse response = Assert.IsType<CompanyResponse>(accepted.Value);
        Assert.Equal(company.Id, response.Id);
        Assert.Equal(2, response.Version);
        Assert.Equal(2, store.SavedEvent?.Version);
        Assert.Equal(company.Id, store.LastExcludedCompanyId);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Update_returns_not_found_or_conflict_without_writing()
    {
        Company existing = Company.Create(
            "Original",
            "B123",
            "Calle 1",
            null,
            null,
            DateTime.UtcNow
        );
        Company duplicateCompany = Company.Create(
            "Otra",
            "C456",
            "Calle 2",
            null,
            null,
            DateTime.UtcNow
        );
        CompanyWriteStore store = new(existing, duplicateCompany);
        CompaniesWriteController controller = CreateController(store, isAdmin: true);

        ActionResult<CompanyResponse> missing = await controller.Update(
            Guid.NewGuid(),
            UpdateRequest("C456"),
            CancellationToken.None
        );
        ActionResult<CompanyResponse> duplicate = await controller.Update(
            existing.Id,
            UpdateRequest("c 456"),
            CancellationToken.None
        );

        Assert.IsType<NotFoundResult>(missing.Result);
        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(duplicate.Result);
        Assert.Equal(
            "Ya existe otra empresa con ese CIF/NIF.",
            Assert.IsType<ProblemDetails>(conflict.Value).Title
        );
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Non_admin_is_forbidden_from_creating_or_updating_companies()
    {
        CompanyWriteStore store = new();
        CompaniesWriteController controller = CreateController(store, isAdmin: false);
        CreateCompanyRequest createRequest = new()
        {
            LegalName = "Restaurantes Norte",
            TaxId = "B123",
            FiscalAddress = "Calle Mayor 1",
        };

        ActionResult<CompanyResponse> create = await controller.Create(
            createRequest,
            CancellationToken.None
        );
        ActionResult<CompanyResponse> update = await controller.Update(
            Guid.NewGuid(),
            UpdateRequest("B123"),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(create.Result);
        Assert.IsType<ForbidResult>(update.Result);
        Assert.Equal(0, store.SaveCount);
    }

    private static CompaniesWriteController CreateController(CompanyWriteStore store, bool isAdmin)
    {
        ClaimsIdentity identity = new(isAdmin ? [new Claim(ClaimTypes.Role, "Admin")] : [], "test");
        return new CompaniesWriteController(new CompanyCommandService(store, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static UpdateCompanyRequest UpdateRequest(string taxId) =>
        new()
        {
            LegalName = "Restaurantes Norte",
            TaxId = taxId,
            FiscalAddress = "Calle Mayor 1",
        };

    private sealed class CompanyWriteStore(params Company[] companies) : ICompanyWriteStore
    {
        private readonly Dictionary<Guid, Company> values = companies.ToDictionary(item => item.Id);

        public CompanyChanged? SavedEvent { get; private set; }
        public Guid? LastExcludedCompanyId { get; private set; }
        public int SaveCount { get; private set; }

        public Task<bool> TaxIdExistsAsync(
            string normalizedTaxId,
            Guid? excludingCompanyId,
            CancellationToken cancellationToken
        )
        {
            LastExcludedCompanyId = excludingCompanyId;
            return Task.FromResult(
                values.Values.Any(company =>
                    company.TaxId == normalizedTaxId && company.Id != excludingCompanyId
                )
            );
        }

        public Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(values.GetValueOrDefault(id));

        public Task SaveWithEventAsync(
            Company company,
            CompanyChanged integrationEvent,
            CancellationToken cancellationToken
        )
        {
            values[company.Id] = company;
            SavedEvent = integrationEvent;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
