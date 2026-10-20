using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Restaurantes.Catalog.Api.Controllers;
using Restaurantes.Catalog.Api.Realtime;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogDesignControllerCoverageTests
{
    [Fact]
    public async Task Snapshot_requires_catalog_management_permission()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: false);

        IActionResult result = await controller.Snapshot(TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "IVA", "IVA", 10)]
    [InlineData("e3a55f9b-f31c-4a03-a9e9-5a8fe2d2e0f2", "bad code", "IVA", 10)]
    [InlineData("e3a55f9b-f31c-4a03-a9e9-5a8fe2d2e0f2", "IVA", " ", 10)]
    [InlineData("e3a55f9b-f31c-4a03-a9e9-5a8fe2d2e0f2", "IVA", "IVA", -1)]
    [InlineData("e3a55f9b-f31c-4a03-a9e9-5a8fe2d2e0f2", "IVA", "IVA", 101)]
    public async Task Save_tax_rejects_invalid_identifier_and_fields(
        string id,
        string code,
        string name,
        decimal rate
    )
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SaveTax(
            Guid.Parse(id),
            new CatalogDesignController.TaxInput(code, name, rate, true),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.Taxes);
    }

    [Theory]
    [InlineData("bad code", "Gluten", "")]
    [InlineData("GLUTEN", " ", "")]
    [InlineData("GLUTEN", "Gluten", "too long")]
    public async Task Save_allergen_rejects_invalid_fields(
        string code,
        string name,
        string description
    )
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);
        if (description == "too long")
            description = new string('x', 501);

        IActionResult result = await controller.SaveAllergen(
            Guid.NewGuid(),
            new CatalogDesignController.AllergenInput(code, name, description, true),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.Allergens);
    }

    [Fact]
    public async Task Save_product_tax_returns_not_found_for_unknown_product()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SaveProductTax(
            Guid.NewGuid(),
            Guid.Empty,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Save_product_tax_rejects_missing_or_inactive_tax()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        CatalogTax inactiveTax = new()
        {
            Code = "IVA",
            Name = "IVA",
            IsActive = false,
        };
        db.AddRange(product, inactiveTax);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult missingTax = await controller.SaveProductTax(
            product.Id,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken
        );
        IActionResult inactiveTaxResult = await controller.SaveProductTax(
            product.Id,
            inactiveTax.Id,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(missingTax);
        Assert.IsType<BadRequestObjectResult>(inactiveTaxResult);
    }

    [Fact]
    public async Task Save_product_allergens_returns_not_found_for_unknown_product()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SaveProductAllergens(
            Guid.NewGuid(),
            [],
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Save_product_allergens_rejects_unknown_or_inactive_allergens()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        CatalogAllergen inactiveAllergen = new()
        {
            Code = "GLUTEN",
            Name = "Gluten",
            IsActive = false,
        };
        db.AddRange(product, inactiveAllergen);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult missing = await controller.SaveProductAllergens(
            product.Id,
            [Guid.NewGuid()],
            TestContext.Current.CancellationToken
        );
        IActionResult inactive = await controller.SaveProductAllergens(
            product.Id,
            [inactiveAllergen.Id],
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(missing);
        Assert.IsType<BadRequestObjectResult>(inactive);
    }

    [Fact]
    public async Task Save_definition_rejects_unknown_product_and_invalid_combo()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);
        CatalogDesignController.DefinitionInput invalidCombo = new(true, false, []);

        IActionResult missingProduct = await controller.SaveDefinition(
            Guid.NewGuid(),
            invalidCombo,
            TestContext.Current.CancellationToken
        );
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        IActionResult comboWithoutChoices = await controller.SaveDefinition(
            product.Id,
            invalidCombo,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(missingProduct);
        Assert.IsType<BadRequestObjectResult>(comboWithoutChoices);
    }

    [Fact]
    public async Task Save_definition_rejects_inconsistent_extra_and_choice_group_rules()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product parent = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(parent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);
        CatalogDesignController.GroupInput validExtra = new(
            "Extras",
            "Extra",
            0,
            1,
            [Guid.NewGuid()]
        );
        CatalogDesignController.GroupInput validChoice = new(
            "Elección",
            "Choice",
            1,
            1,
            [Guid.NewGuid()]
        );
        CatalogDesignController.DefinitionInput[] invalidDefinitions =
        [
            new(false, true, []),
            new(true, false, [validExtra]),
            new(true, true, [validExtra with { Name = " " }]),
            new(true, true, [validExtra with { Kind = "Unknown" }]),
            new(true, true, [validExtra with { MinSelections = -1 }]),
            new(true, true, [validExtra with { MaxSelections = 0 }]),
            new(true, true, [validExtra with { MinSelections = 2, MaxSelections = 1 }]),
            new(
                true,
                true,
                [
                    validExtra with
                    {
                        ProductIds = [Guid.NewGuid(), Guid.NewGuid()],
                        MaxSelections = 3,
                    },
                ]
            ),
            new(true, true, [validExtra with { ProductIds = [parent.Id] }]),
            new(false, true, [validChoice]),
            new(true, true, [validChoice with { MinSelections = 0 }]),
            new(true, true, [validChoice with { MaxSelections = 2 }]),
        ];

        foreach (CatalogDesignController.DefinitionInput definition in invalidDefinitions)
        {
            IActionResult result = await controller.SaveDefinition(
                parent.Id,
                definition,
                TestContext.Current.CancellationToken
            );

            Assert.IsType<BadRequestObjectResult>(result);
        }
        Assert.Empty(db.ProductConfigurations);
    }

    [Fact]
    public async Task Save_prices_rejects_unknown_product_and_invalid_channel()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);
        CatalogDesignController.PriceInput[] invalidPrices = [new("UNKNOWN", "PLATO", 10m)];

        IActionResult missingProduct = await controller.SavePrices(
            Guid.NewGuid(),
            Guid.Empty,
            [.. invalidPrices],
            TestContext.Current.CancellationToken
        );
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        IActionResult invalidChannel = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [.. invalidPrices],
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NotFoundResult>(missingProduct);
        Assert.IsType<BadRequestObjectResult>(invalidChannel);
    }

    [Fact]
    public async Task Set_local_availability_requires_an_active_global_menu()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SetLocalAvailability(
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Set_local_availability_rejects_missing_and_inactive_global_products()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Product inactiveProduct = Product.Create(
            "P-1",
            "Producto inactivo",
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        inactiveProduct.SetActive(false, DateTime.UtcNow);
        db.AddRange(
            new CatalogMenu { RestaurantId = Guid.Empty, Name = "Carta global" },
            inactiveProduct
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult missingProduct = await controller.SetLocalAvailability(
            restaurantId,
            Guid.NewGuid(),
            false,
            TestContext.Current.CancellationToken
        );
        IActionResult inactiveProductResult = await controller.SetLocalAvailability(
            restaurantId,
            inactiveProduct.Id,
            true,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(missingProduct);
        Assert.IsType<BadRequestObjectResult>(inactiveProductResult);
    }

    [Fact]
    public async Task Snapshot_returns_catalog_data_for_global_administrator()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        db.Products.Add(Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow));
        db.Taxes.Add(
            new CatalogTax
            {
                Code = "IVA",
                Name = "IVA",
                Rate = 21m,
            }
        );
        db.Allergens.Add(new CatalogAllergen { Code = "GLUTEN", Name = "Gluten" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.Snapshot(TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Save_tax_rejects_duplicate_normalized_code()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        db.Taxes.Add(
            new CatalogTax
            {
                Code = "IVA",
                Name = "Existente",
                Rate = 21m,
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SaveTax(
            Guid.NewGuid(),
            new CatalogDesignController.TaxInput("iva", "Nuevo", 10m, true),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Save_tax_creates_and_updates_normalized_catalog_tax()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);
        Guid taxId = Guid.NewGuid();

        IActionResult created = await controller.SaveTax(
            taxId,
            new CatalogDesignController.TaxInput("iva", "  IVA General ", 21m, true),
            TestContext.Current.CancellationToken
        );
        IActionResult updated = await controller.SaveTax(
            taxId,
            new CatalogDesignController.TaxInput("IVA", "IVA reducido", 10m, false),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            "IVA",
            Assert.IsType<CatalogTax>(Assert.IsType<OkObjectResult>(created).Value).Code
        );
        CatalogTax saved = Assert.IsType<CatalogTax>(Assert.IsType<OkObjectResult>(updated).Value);
        Assert.Equal("IVA", saved.Code);
        Assert.Equal("IVA reducido", saved.Name);
        Assert.Equal(10m, saved.Rate);
        Assert.False(saved.IsActive);
        Assert.Single(db.Taxes);
    }

    [Fact]
    public async Task Save_product_tax_assigns_and_clears_tax()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        CatalogTax tax = new()
        {
            Code = "IVA",
            Name = "IVA general",
            Rate = 21m,
        };
        db.AddRange(product, tax);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult assigned = await controller.SaveProductTax(
            product.Id,
            tax.Id,
            TestContext.Current.CancellationToken
        );
        Assert.IsType<NoContentResult>(assigned);
        Assert.Equal(tax.Id, product.TaxId);

        IActionResult cleared = await controller.SaveProductTax(
            product.Id,
            Guid.Empty,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NoContentResult>(cleared);
        Assert.Null(product.TaxId);
    }

    [Fact]
    public async Task Set_local_availability_creates_and_updates_restaurant_selection()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Product product = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        db.AddRange(
            product,
            new CatalogMenu
            {
                RestaurantId = Guid.Empty,
                Name = "Carta global",
                IsActive = true,
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult created = await controller.SetLocalAvailability(
            restaurantId,
            product.Id,
            false,
            TestContext.Current.CancellationToken
        );
        IActionResult updated = await controller.SetLocalAvailability(
            restaurantId,
            product.Id,
            true,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<NoContentResult>(created);
        Assert.IsType<NoContentResult>(updated);
        RestaurantProductAvailability availability = Assert.Single(
            db.RestaurantProductAvailabilities
        );
        Assert.Equal(restaurantId, availability.RestaurantId);
        Assert.Equal(product.Id, availability.ProductId);
        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task Save_allergen_normalizes_new_values_and_rejects_duplicate_code()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        db.Allergens.Add(
            new CatalogAllergen
            {
                Code = "GLUTEN",
                Name = "Gluten existente",
                Description = "Descripción",
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult duplicate = await controller.SaveAllergen(
            Guid.NewGuid(),
            new CatalogDesignController.AllergenInput("gluten", "Otro", "", true),
            TestContext.Current.CancellationToken
        );
        IActionResult created = await controller.SaveAllergen(
            Guid.NewGuid(),
            new CatalogDesignController.AllergenInput("nuts", " Frutos secos ", "  Traza  ", true),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ConflictObjectResult>(duplicate);
        CatalogAllergen saved = Assert.IsType<CatalogAllergen>(
            Assert.IsType<OkObjectResult>(created).Value
        );
        Assert.Equal("NUTS", saved.Code);
        Assert.Equal("Frutos secos", saved.Name);
        Assert.Equal("Traza", saved.Description);
    }

    [Fact]
    public async Task Save_definition_rejects_invalid_groups_missing_options_tax_and_extra_price()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product parent = Product.Create("P-1", "Producto", Guid.NewGuid(), DateTime.UtcNow);
        Product extraWithoutTax = Product.Create(
            "E-1",
            "Extra sin impuesto",
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        CatalogTax tax = new()
        {
            Code = "IVA",
            Name = "IVA",
            Rate = 21m,
        };
        Product extraWithoutPrice = Product.Create(
            "E-2",
            "Extra sin precio",
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        extraWithoutPrice.AssignTax(tax.Id);
        db.AddRange(parent, extraWithoutTax, tax, extraWithoutPrice);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        CatalogDesignController.GroupInput invalidGroup = new(
            "Extras",
            "Extra",
            2,
            1,
            [extraWithoutTax.Id]
        );
        CatalogDesignController.GroupInput validExtraWithoutTax = new(
            "Extras",
            "Extra",
            0,
            1,
            [extraWithoutTax.Id]
        );
        CatalogDesignController.GroupInput validExtraWithoutPrice = new(
            "Extras",
            "Extra",
            0,
            1,
            [extraWithoutPrice.Id]
        );

        IActionResult invalidGroupResult = await controller.SaveDefinition(
            parent.Id,
            new CatalogDesignController.DefinitionInput(true, true, [invalidGroup]),
            TestContext.Current.CancellationToken
        );
        IActionResult unknownOption = await controller.SaveDefinition(
            parent.Id,
            new CatalogDesignController.DefinitionInput(
                false,
                true,
                [validExtraWithoutTax with { ProductIds = [Guid.NewGuid()] }]
            ),
            TestContext.Current.CancellationToken
        );
        IActionResult missingTax = await controller.SaveDefinition(
            parent.Id,
            new CatalogDesignController.DefinitionInput(false, true, [validExtraWithoutTax]),
            TestContext.Current.CancellationToken
        );
        IActionResult missingPrice = await controller.SaveDefinition(
            parent.Id,
            new CatalogDesignController.DefinitionInput(false, true, [validExtraWithoutPrice]),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(invalidGroupResult);
        Assert.IsType<BadRequestObjectResult>(unknownOption);
        Assert.IsType<BadRequestObjectResult>(missingTax);
        Assert.IsType<BadRequestObjectResult>(missingPrice);
    }

    [Fact]
    public async Task Save_prices_rejects_duplicate_channels_and_invalid_extra_format()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Guid categoryId = Guid.NewGuid();
        Product product = Product.Create("P-1", "Producto", categoryId, DateTime.UtcNow);
        Product extra = Product.Create("E-1", "Extra", categoryId, DateTime.UtcNow);
        Category extrasCategory = Category.Create(
            "EXTRAS",
            "Extras",
            "",
            "",
            DateTime.UtcNow,
            requiresKds: false
        );
        extra.SetCategory(extrasCategory.Id, DateTime.UtcNow);
        db.AddRange(product, extra, extrasCategory);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult duplicate = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [new("LOCAL", "PLATO", 10m), new("LOCAL", "PLATO", 12m)],
            TestContext.Current.CancellationToken
        );
        IActionResult invalidExtraFormat = await controller.SavePrices(
            extra.Id,
            Guid.Empty,
            [new("LOCAL", "PLATO", 1m)],
            TestContext.Current.CancellationToken
        );
        IActionResult invalidChannel = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [new("UNKNOWN", "PLATO", 10m)],
            TestContext.Current.CancellationToken
        );
        IActionResult invalidFormat = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [new("LOCAL", "UNKNOWN", 10m)],
            TestContext.Current.CancellationToken
        );
        IActionResult negativeAmount = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [new("LOCAL", "PLATO", -1m)],
            TestContext.Current.CancellationToken
        );
        IActionResult excessiveAmount = await controller.SavePrices(
            product.Id,
            Guid.Empty,
            [new("LOCAL", "PLATO", 10_000_000_000m)],
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BadRequestObjectResult>(duplicate);
        Assert.IsType<BadRequestObjectResult>(invalidExtraFormat);
        Assert.IsType<BadRequestObjectResult>(invalidChannel);
        Assert.IsType<BadRequestObjectResult>(invalidFormat);
        Assert.IsType<BadRequestObjectResult>(negativeAmount);
        Assert.IsType<BadRequestObjectResult>(excessiveAmount);
    }

    [Fact]
    public async Task Set_local_availability_forbids_global_scope()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogDesignController controller = CreateController(db, isAdmin: true);

        IActionResult result = await controller.SetLocalAvailability(
            Guid.Empty,
            Guid.NewGuid(),
            true,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result);
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

    private static CatalogDesignController CreateController(
        CatalogWriteDbContext db,
        bool isAdmin
    ) =>
        new(
            db,
            new CatalogMenuSynchronizer(db),
            new CatalogNotifications(null!, NullLogger<CatalogNotifications>.Instance)
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        isAdmin
                            ? new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")
                            : new ClaimsIdentity()
                    ),
                },
            },
        };
}
