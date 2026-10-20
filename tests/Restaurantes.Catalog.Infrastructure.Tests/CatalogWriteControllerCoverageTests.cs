using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Restaurantes.Catalog.Api.Controllers;
using Restaurantes.Catalog.Api.Realtime;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogWriteControllerCoverageTests
{
    [Fact]
    public async Task CreateCategory_checks_permission_then_returns_accepted_or_conflict()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogWriteController forbiddenController = CreateController(
            db,
            Guid.Empty,
            isAdmin: false
        );

        var forbidden = await forbiddenController.CreateCategory(
            new CreateCategoryRequest { Code = "POSTRES", Name = "Postres" },
            CancellationToken.None
        );
        Assert.IsType<ForbidResult>(forbidden.Result);

        CatalogWriteController controller = CreateController(db, Guid.Empty, isAdmin: true);
        var accepted = await controller.CreateCategory(
            new CreateCategoryRequest { Code = "POSTRES", Name = "Postres" },
            CancellationToken.None
        );
        var conflict = await controller.CreateCategory(
            new CreateCategoryRequest { Code = "postres", Name = "Duplicado" },
            CancellationToken.None
        );

        Assert.IsType<AcceptedResult>(accepted.Result);
        ProblemDetails details = Assert.IsType<ProblemDetails>(
            Assert.IsType<ConflictObjectResult>(conflict.Result).Value
        );
        Assert.Equal("Category code already exists", details.Title);
        Assert.Single(db.Categories);
        Assert.Single(db.OutboxMessages);
    }

    [Fact]
    public async Task CreateProduct_rejects_permission_and_duplicate_or_missing_sku_and_creates_product()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Category category = Category.Create(
            "BEBIDAS",
            "Bebidas",
            "BEBIDAS",
            "Bebidas",
            DateTime.UtcNow
        );
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogWriteController forbiddenController = CreateController(
            db,
            Guid.Empty,
            isAdmin: false
        );
        var forbidden = await forbiddenController.CreateProduct(
            new CreateProductRequest
            {
                Sku = "AGUA",
                Name = "Agua",
                CategoryId = category.Id,
            },
            CancellationToken.None
        );
        Assert.IsType<ForbidResult>(forbidden.Result);

        CatalogWriteController controller = CreateController(db, Guid.Empty, isAdmin: true);
        var accepted = await controller.CreateProduct(
            new CreateProductRequest
            {
                Sku = "AGUA",
                Name = "Agua",
                CategoryId = category.Id,
            },
            CancellationToken.None
        );
        var duplicate = await controller.CreateProduct(
            new CreateProductRequest
            {
                Sku = "agua",
                Name = "Duplicada",
                CategoryId = category.Id,
            },
            CancellationToken.None
        );
        var missingCategory = await controller.CreateProduct(
            new CreateProductRequest
            {
                Sku = "LIMONADA",
                Name = "Limonada",
                CategoryId = Guid.NewGuid(),
            },
            CancellationToken.None
        );

        Assert.IsType<AcceptedResult>(accepted.Result);
        Assert.IsType<ConflictObjectResult>(duplicate.Result);
        Assert.IsType<ConflictObjectResult>(missingCategory.Result);
        Assert.Single(db.Products);
        Assert.Single(db.OutboxMessages);
    }

    [Fact]
    public async Task Global_product_updates_return_not_found_or_synchronize_and_accept()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogWriteController controller = CreateController(db, Guid.Empty, isAdmin: true);
        Guid missingId = Guid.NewGuid();

        Assert.IsType<NotFoundResult>(
            (
                await controller.UpdateProduct(
                    missingId,
                    new UpdateProductRequest { Name = "Nuevo", CategoryId = Guid.NewGuid() },
                    CancellationToken.None
                )
            ).Result
        );

        Category category = Category.Create(
            "PLATOS",
            "Platos",
            "COCINA",
            "Cocina",
            DateTime.UtcNow
        );
        Product product = Product.Create("P-1", "Pasta", category.Id, DateTime.UtcNow);
        db.AddRange(category, product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await controller.UpdateProduct(
            product.Id,
            new UpdateProductRequest { Name = "Pasta fresca", CategoryId = category.Id },
            CancellationToken.None
        );
        var renamed = await controller.SetProductName(
            product.Id,
            "Ravioli",
            CancellationToken.None
        );
        var deactivated = await controller.SetProductActive(
            product.Id,
            false,
            CancellationToken.None
        );

        Assert.IsType<AcceptedResult>(updated.Result);
        Assert.IsType<AcceptedResult>(renamed.Result);
        Assert.IsType<AcceptedResult>(deactivated.Result);
        Assert.Equal("Ravioli", product.Name);
        Assert.False(product.IsActive);
        Assert.Equal(3, db.OutboxMessages.Count());
    }

    [Fact]
    public async Task Configure_requires_scoped_permission_and_reports_missing_products()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        CatalogWriteController forbiddenController = CreateController(
            db,
            restaurantId,
            isAdmin: false
        );
        var forbidden = await forbiddenController.Configure(
            restaurantId,
            Guid.NewGuid(),
            new ConfigureMenuItemRequest { Price = 10m },
            CancellationToken.None
        );
        Assert.IsType<ForbidResult>(forbidden.Result);

        CatalogWriteController controller = CreateController(
            db,
            restaurantId,
            isAdmin: false,
            hasCatalogPermission: true
        );
        var missing = await controller.Configure(
            restaurantId,
            Guid.NewGuid(),
            new ConfigureMenuItemRequest { Price = 10m },
            CancellationToken.None
        );

        ProblemDetails details = Assert.IsType<ProblemDetails>(
            Assert.IsType<NotFoundObjectResult>(missing.Result).Value
        );
        Assert.Equal("Product or category was not found or is inactive", details.Title);
    }

    [Fact]
    public async Task Station_routes_enforce_manager_scope_and_create_update_and_change_station()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        CatalogWriteController adminController = CreateController(db, restaurantId, isAdmin: true);
        var adminForbidden = await adminController.CreateStation(
            restaurantId,
            new CreateKitchenStationRequest { Code = "COCINA", Name = "Cocina" },
            CancellationToken.None
        );
        Assert.IsType<ForbidResult>(adminForbidden.Result);

        CatalogWriteController controller = CreateController(
            db,
            restaurantId,
            isAdmin: false,
            hasCatalogPermission: true
        );
        var created = await controller.CreateStation(
            restaurantId,
            new CreateKitchenStationRequest
            {
                Code = "COCINA",
                Name = "Cocina",
                Priority = 1,
            },
            CancellationToken.None
        );
        KitchenStationResponse station = Assert.IsType<KitchenStationResponse>(
            Assert.IsType<AcceptedResult>(created.Result).Value
        );
        var duplicate = await controller.CreateStation(
            restaurantId,
            new CreateKitchenStationRequest { Code = "cocina", Name = "Duplicada" },
            CancellationToken.None
        );
        Assert.IsType<ConflictObjectResult>(duplicate.Result);
        var secondaryCreated = await controller.CreateStation(
            restaurantId,
            new CreateKitchenStationRequest
            {
                Code = "BEBIDAS",
                Name = "Bebidas",
                Priority = 2,
            },
            CancellationToken.None
        );
        KitchenStationResponse secondary = Assert.IsType<KitchenStationResponse>(
            Assert.IsType<AcceptedResult>(secondaryCreated.Result).Value
        );

        var updated = await controller.UpdateStation(
            restaurantId,
            station.Id,
            new UpdateKitchenStationRequest { Name = "Cocina central", IsActive = true },
            CancellationToken.None
        );
        var notFound = await controller.UpdateStation(
            restaurantId,
            Guid.NewGuid(),
            new UpdateKitchenStationRequest { Name = "No existe", IsActive = true },
            CancellationToken.None
        );
        Assert.IsType<AcceptedResult>(updated.Result);
        Assert.IsType<NotFoundResult>(notFound.Result);

        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationName(
                    restaurantId,
                    station.Id,
                    "Caliente",
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationPriority(
                    restaurantId,
                    station.Id,
                    5,
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationActive(
                    restaurantId,
                    station.Id,
                    false,
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationPrimary(
                    restaurantId,
                    station.Id,
                    true,
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationRequiresPrimaryDispatch(
                    restaurantId,
                    secondary.Id,
                    true,
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<AcceptedResult>(
            (
                await controller.SetStationPrintTicket(
                    restaurantId,
                    station.Id,
                    true,
                    CancellationToken.None
                )
            ).Result
        );
        Assert.IsType<ForbidResult>(
            (
                await controller.SetStationName(
                    Guid.NewGuid(),
                    station.Id,
                    "Denied",
                    CancellationToken.None
                )
            ).Result
        );
    }

    private static CatalogWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
                )
                .Options
        );

    private static CatalogWriteController CreateController(
        CatalogWriteDbContext db,
        Guid restaurantId,
        bool isAdmin,
        bool hasCatalogPermission = false
    )
    {
        List<Claim> claims = [];
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }
        else if (restaurantId != Guid.Empty)
        {
            claims.Add(new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:Manager"));
            if (hasCatalogPermission)
            {
                claims.Add(
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(
                            restaurantId,
                            RestaurantPermissions.CatalogManage
                        )
                    )
                );
            }
        }

        return new CatalogWriteController(
            new CatalogCommandService(new CatalogWriteStore(db), TimeProvider.System),
            new CatalogMenuSynchronizer(db),
            new CatalogNotifications(null!, NullLogger<CatalogNotifications>.Instance),
            db
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
    }
}
