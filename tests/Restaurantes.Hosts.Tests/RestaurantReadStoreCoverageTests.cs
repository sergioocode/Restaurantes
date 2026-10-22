using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantReadStoreCoverageTests
{
    [Fact]
    public async Task Company_store_returns_single_company_and_lists_companies_by_legal_name()
    {
        await using RestaurantReadDbContext db = CreateDatabase();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        DateTime updatedAtUtc = DateTime.UtcNow;
        db.Companies.AddRange(
            new CompanyReadModel
            {
                Id = firstId,
                LegalName = "Zulu, S.L.",
                TaxId = "B00000001",
                FiscalAddress = "Calle Z",
                Phone = "600000001",
                Email = "zulu@example.test",
                Version = 2,
                UpdatedAtUtc = updatedAtUtc,
            },
            new CompanyReadModel
            {
                Id = secondId,
                LegalName = "Alfa, S.L.",
                TaxId = "B00000002",
                FiscalAddress = "Calle A",
                Version = 3,
                UpdatedAtUtc = updatedAtUtc,
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CompanyReadStore store = new(db);

        var found = await store.FindAsync(firstId, TestContext.Current.CancellationToken);
        var missing = await store.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        IReadOnlyList<Restaurantes.RestaurantOperations.Contracts.Responses.CompanyResponse> listed =
            await store.ListAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("Zulu, S.L.", found.LegalName);
        Assert.Equal("B00000001", found.TaxId);
        Assert.Equal("Calle Z", found.FiscalAddress);
        Assert.Equal("600000001", found.Phone);
        Assert.Equal("zulu@example.test", found.Email);
        Assert.Equal(2, found.Version);
        Assert.Equal(updatedAtUtc, found.UpdatedAtUtc);
        Assert.Null(missing);
        Assert.Equal(["Alfa, S.L.", "Zulu, S.L."], listed.Select(company => company.LegalName));
        Assert.Null(listed[0].Phone);
        Assert.Null(listed[0].Email);
    }

    [Fact]
    public async Task Find_returns_restaurant_with_optional_company_details()
    {
        await using RestaurantReadDbContext db = CreateDatabase();
        Guid companyId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        db.Companies.Add(
            new CompanyReadModel
            {
                Id = companyId,
                LegalName = "Empresa de prueba",
                TaxId = "B12345678",
                FiscalAddress = "Calle fiscal",
                Phone = "600000000",
                Email = "empresa@example.test",
            }
        );
        db.Restaurants.AddRange(
            Restaurant(restaurantId, "Restaurante vinculado", companyId),
            Restaurant(Guid.NewGuid(), "Restaurante independiente", null)
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        RestaurantReadStore store = new(db);

        var linked = await store.FindAsync(restaurantId, TestContext.Current.CancellationToken);
        var missing = await store.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.NotNull(linked);
        Assert.Equal("R-1", linked.Code);
        Assert.Equal("Restaurante vinculado", linked.Name);
        Assert.Equal("Calle 1", linked.Address);
        Assert.Equal(3, linked.Version);
        Assert.Equal("Empresa de prueba", linked.Company!.LegalName);
        Assert.Equal("B12345678", linked.Company.TaxId);
        Assert.Equal("empresa@example.test", linked.Company.Email);
        Assert.Null(missing);
    }

    [Fact]
    public async Task List_orders_by_name_and_includes_or_omits_company_projection()
    {
        await using RestaurantReadDbContext db = CreateDatabase();
        Guid companyId = Guid.NewGuid();
        db.Companies.Add(
            new CompanyReadModel
            {
                Id = companyId,
                LegalName = "Empresa",
                TaxId = "B12345678",
                FiscalAddress = "Calle fiscal",
            }
        );
        db.Restaurants.AddRange(
            Restaurant(Guid.NewGuid(), "Zulu", null),
            Restaurant(Guid.NewGuid(), "Alfa", companyId)
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        RestaurantReadStore store = new(db);

        IReadOnlyList<Restaurantes.RestaurantOperations.Contracts.Responses.RestaurantResponse> result =
            await store.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Alfa", "Zulu"], result.Select(item => item.Name));
        Assert.NotNull(result[0].Company);
        Assert.Null(result[1].Company);
    }

    private static RestaurantReadDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<RestaurantReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static RestaurantReadModel Restaurant(Guid id, string name, Guid? companyId) =>
        new()
        {
            Id = id,
            Code = "R-1",
            Name = name,
            Address = "Calle 1",
            CompanyId = companyId,
            IsActive = true,
            Version = 3,
            UpdatedAtUtc = DateTime.UtcNow,
        };
}
