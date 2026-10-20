using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Domain;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantOperationsCommandServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedTimeProvider(Now);

    [Fact]
    public async Task Company_create_normalizes_tax_id_and_publishes_the_created_version()
    {
        CompanyStore store = new();
        CompanyCommandService service = new(store, Clock);

        var result = await service.CreateAsync(
            new CreateCompanyRequest
            {
                LegalName = "  Restaurante Uno  ",
                TaxId = "  b 123  ",
                FiscalAddress = "  Calle Mayor 1  ",
                Phone = "  600 123  ",
                Email = "  hola@example.test  ",
            },
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("Restaurante Uno", result.LegalName);
        Assert.Equal("B123", result.TaxId);
        Assert.Equal("Calle Mayor 1", result.FiscalAddress);
        Assert.Equal("600 123", result.Phone);
        Assert.Equal("hola@example.test", result.Email);
        Assert.Equal(1, result.Version);
        Assert.Equal(Now, result.UpdatedAtUtc);
        Assert.Equal(result.Id, store.SavedCompany?.Id);
        Assert.Equal(result.Id, store.SavedEvent?.CompanyId);
        Assert.Equal("B123", store.SavedEvent?.TaxId);
        Assert.Equal(1, store.SavedEvent?.Version);
    }

    [Fact]
    public async Task Company_create_and_update_reject_duplicate_tax_ids_without_writing()
    {
        Company existing = Company.Create("Uno", "ABC", "Calle 1", null, null, Now);
        Company toUpdate = Company.Create("Dos", "XYZ", "Calle 2", null, null, Now);
        CompanyStore store = new(existing, toUpdate);
        CompanyCommandService service = new(store, Clock);

        Assert.Null(
            await service.CreateAsync(
                new CreateCompanyRequest
                {
                    LegalName = "Dos",
                    TaxId = " a b c ",
                    FiscalAddress = "Calle 2",
                },
                CancellationToken.None
            )
        );
        Assert.Null(
            await service.UpdateAsync(
                toUpdate.Id,
                new UpdateCompanyRequest
                {
                    LegalName = "Dos",
                    TaxId = "ABC",
                    FiscalAddress = "Calle 2",
                },
                CancellationToken.None
            )
        );
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Company_update_excludes_its_own_tax_id_and_increments_the_version()
    {
        Company existing = Company.Create("Original", "abc", "Calle 1", null, null, Now);
        CompanyStore store = new(existing);
        CompanyCommandService service = new(store, Clock);

        var result = await service.UpdateAsync(
            existing.Id,
            new UpdateCompanyRequest
            {
                LegalName = "  Actualizada ",
                TaxId = " a b c ",
                FiscalAddress = " Avenida 2 ",
                Phone = " ",
                Email = " nuevo@example.test ",
            },
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("Actualizada", result.LegalName);
        Assert.Equal("ABC", result.TaxId);
        Assert.Null(result.Phone);
        Assert.Equal("nuevo@example.test", result.Email);
        Assert.Equal(2, result.Version);
        Assert.Equal(Now, result.UpdatedAtUtc);
        Assert.Equal(existing.Id, store.LastExcludedCompanyId);
        Assert.Equal(2, store.SavedEvent?.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Company_exists_reports_store_lookup(bool exists)
    {
        Company company = Company.Create("Empresa", "ABC", "Calle", null, null, Now);
        CompanyStore store = new(exists ? [company] : []);
        CompanyCommandService service = new(store, Clock);

        Assert.Equal(await service.ExistsAsync(company.Id, CancellationToken.None), exists);
    }

    [Fact]
    public async Task Restaurant_create_checks_code_and_company_then_publishes_normalized_values()
    {
        Guid companyId = Guid.NewGuid();
        RestaurantStore store = new(companyId);
        RestaurantCommandService service = new(store, Clock);

        var result = await service.CreateAsync(
            new CreateRestaurantRequest
            {
                Code = "  local-1 ",
                Name = "  Centro ",
                Address = " Calle 1 ",
                CompanyId = companyId,
            },
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("LOCAL-1", result.Code);
        Assert.Equal("Centro", result.Name);
        Assert.Equal("Calle 1", result.Address);
        Assert.True(result.IsActive);
        Assert.Equal(1, result.Version);
        Assert.Equal(Now, result.UpdatedAtUtc);
        Assert.Equal(result.Id, store.SavedEvent?.RestaurantId);
        Assert.Equal(companyId, store.SavedEvent?.CompanyId);
        Assert.True(store.SavedEvent?.IsActive);
    }

    [Fact]
    public async Task Restaurant_create_rejects_duplicate_codes_and_unknown_companies()
    {
        Guid companyId = Guid.NewGuid();
        RestaurantStore store = new(companyId) { ExistingCode = "LOCAL-1" };
        RestaurantCommandService service = new(store, Clock);

        Assert.Null(
            await service.CreateAsync(
                CreateRestaurant(" local-1 ", companyId),
                CancellationToken.None
            )
        );
        Assert.Null(
            await service.CreateAsync(
                CreateRestaurant("LOCAL-2", Guid.NewGuid()),
                CancellationToken.None
            )
        );
        Assert.Null(
            await service.CreateAsync(
                new CreateRestaurantRequest { Code = "LOCAL-3", CompanyId = null },
                CancellationToken.None
            )
        );
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Restaurant_updates_publish_each_mutation_and_missing_ids_return_null()
    {
        Guid companyId = Guid.NewGuid();
        Restaurant restaurant = Restaurant.Create("LOCAL-1", "Original", "Calle 1", companyId, Now);
        RestaurantStore store = new(companyId, restaurant);
        RestaurantCommandService service = new(store, Clock);

        Assert.Null(
            await service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateRestaurantRequest { Name = "Missing", Address = "Calle" },
                CancellationToken.None
            )
        );
        Assert.Null(
            await service.SetCompanyAsync(restaurant.Id, Guid.NewGuid(), CancellationToken.None)
        );
        Assert.Null(await service.SetActiveAsync(Guid.NewGuid(), false, CancellationToken.None));

        var updated = await service.UpdateAsync(
            restaurant.Id,
            new UpdateRestaurantRequest { Name = " Actualizado ", Address = " Avenida 2 " },
            CancellationToken.None
        );
        Guid newCompanyId = Guid.NewGuid();
        store.AddCompany(newCompanyId);
        var reassigned = await service.SetCompanyAsync(
            restaurant.Id,
            newCompanyId,
            CancellationToken.None
        );
        var deactivated = await service.SetActiveAsync(
            restaurant.Id,
            false,
            CancellationToken.None
        );

        Assert.Equal("Actualizado", updated?.Name);
        Assert.Equal("Avenida 2", updated?.Address);
        Assert.Equal(2, updated?.Version);
        Assert.Equal(3, reassigned?.Version);
        Assert.NotEqual(companyId, reassigned?.CompanyId);
        Assert.False(deactivated?.IsActive);
        Assert.Equal(4, deactivated?.Version);
        Assert.Equal(Now, deactivated?.UpdatedAtUtc);
        Assert.Equal(3, store.SaveCount);
        Assert.False(store.SavedEvent?.IsActive);
    }

    private static CreateRestaurantRequest CreateRestaurant(string code, Guid companyId) =>
        new()
        {
            Code = code,
            Name = "Local",
            Address = "Calle 1",
            CompanyId = companyId,
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class CompanyStore(params Company[] companies) : ICompanyWriteStore
    {
        private readonly Dictionary<Guid, Company> _companies = companies.ToDictionary(x => x.Id);

        public Company? SavedCompany { get; private set; }
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
                _companies.Values.Any(company =>
                    company.TaxId == normalizedTaxId && company.Id != excludingCompanyId
                )
            );
        }

        public Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_companies.GetValueOrDefault(id));

        public Task SaveWithEventAsync(
            Company company,
            CompanyChanged integrationEvent,
            CancellationToken cancellationToken
        )
        {
            _companies[company.Id] = company;
            SavedCompany = company;
            SavedEvent = integrationEvent;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RestaurantStore(Guid companyId, params Restaurant[] restaurants)
        : IRestaurantWriteStore
    {
        private readonly Dictionary<Guid, Restaurant> _restaurants = restaurants.ToDictionary(x =>
            x.Id
        );

        public string? ExistingCode { get; init; }
        public RestaurantChanged? SavedEvent { get; private set; }
        public int SaveCount { get; private set; }

        public Task<bool> CodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                ExistingCode == normalizedCode
                    || _restaurants.Values.Any(x => x.Code == normalizedCode)
            );

        private readonly HashSet<Guid> _companyIds = [companyId];

        public void AddCompany(Guid id) => _companyIds.Add(id);

        public Task<bool> CompanyExistsAsync(
            Guid requestedCompanyId,
            CancellationToken cancellationToken
        ) => Task.FromResult(_companyIds.Contains(requestedCompanyId));

        public Task<Restaurant?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_restaurants.GetValueOrDefault(id));

        public Task SaveWithEventAsync(
            Restaurant restaurant,
            RestaurantChanged integrationEvent,
            CancellationToken cancellationToken
        )
        {
            _restaurants[restaurant.Id] = restaurant;
            SavedEvent = integrationEvent;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
