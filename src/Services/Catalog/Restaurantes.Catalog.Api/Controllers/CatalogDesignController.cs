using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Controllers;

[ApiController, Authorize, Route("api/catalog/design")]
public sealed class CatalogDesignController(
    CatalogWriteDbContext db,
    CatalogMenuSynchronizer synchronizer
) : ControllerBase
{
    private static readonly HashSet<string> Formats =
    [
        "PLATO",
        "RACION",
        "TAPA",
        "MEDIA_RACION",
        "UNIDAD",
        "EXTRA",
        "COMBO",
    ];
    private static readonly HashSet<string> Channels = ["LOCAL", "DELIVERY", "GLOVO", "UBER"];
    private static readonly HashSet<string> Platforms = ["GLOVO", "UBER"];

    private bool CanManageGlobal() =>
        User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage);

    private bool CanManageScope(Guid restaurantId) =>
        restaurantId == Guid.Empty
            ? CanManageGlobal()
            : User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage);

    [HttpGet]
    public async Task<IActionResult> Snapshot(CancellationToken ct)
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogManage))
        {
            return Forbid();
        }

        CatalogPrice[] prices = await db.Prices.AsNoTracking().ToArrayAsync(ct);
        CatalogMenu[] menus = await db.CatalogMenus.AsNoTracking().ToArrayAsync(ct);
        Guid[] visibleMenuIds = menus
            .Where(m => m.RestaurantId == Guid.Empty || CanManageScope(m.RestaurantId))
            .Select(m => m.Id)
            .ToArray();
        CatalogMenuEntry[] entries = await db
            .CatalogMenuEntries.AsNoTracking()
            .Where(e => visibleMenuIds.Contains(e.MenuId))
            .ToArrayAsync(ct);
        Guid[] entryIds = entries.Select(e => e.Id).ToArray();
        return Ok(
            new
            {
                taxes = await db.Taxes.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(ct),
                allergens = await db.Allergens.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(ct),
                productTaxes = await db
                    .Products.AsNoTracking()
                    .Select(x => new { ProductId = x.Id, TaxId = x.TaxId ?? Guid.Empty })
                    .ToArrayAsync(ct),
                productAllergens = await db.ProductAllergens.AsNoTracking().ToArrayAsync(ct),
                productPhotos = await db.ProductPhotos.AsNoTracking().ToArrayAsync(ct),
                configurations = await db.ProductConfigurations.AsNoTracking().ToArrayAsync(ct),
                groups = await db.ProductOptionGroups.AsNoTracking().ToArrayAsync(ct),
                options = await db.ProductOptions.AsNoTracking().ToArrayAsync(ct),
                prices = prices.Where(p =>
                    p.RestaurantId == Guid.Empty || CanManageScope(p.RestaurantId)
                ),
                menus = menus.Where(m =>
                    m.RestaurantId == Guid.Empty || CanManageScope(m.RestaurantId)
                ),
                entries,
                menuPrices = await db
                    .CatalogMenuPrices.AsNoTracking()
                    .Where(x => visibleMenuIds.Contains(x.MenuId))
                    .ToArrayAsync(ct),
                menuChannels = await db
                    .CatalogMenuChannels.AsNoTracking()
                    .Where(x => entryIds.Contains(x.MenuEntryId))
                    .ToArrayAsync(ct),
            }
        );
    }

    [HttpPut("taxes/{id:guid}")]
    public async Task<IActionResult> SaveTax(Guid id, TaxInput input, CancellationToken ct)
    {
        if (!CanManageGlobal())
            return Forbid();
        if (
            id == Guid.Empty
            || !ValidCode(input.Code)
            || !ValidName(input.Name)
            || input.Rate is < 0 or > 100
        )
            return BadRequest("Código, nombre o porcentaje de impuesto inválido.");
        string code = input.Code.Trim().ToUpperInvariant();
        if (await db.Taxes.AnyAsync(x => x.Code == code && x.Id != id, ct))
            return Conflict("El código de impuesto ya existe.");
        CatalogTax? tax = await db.Taxes.FindAsync([id], ct);
        if (tax is null)
        {
            tax = new CatalogTax { Id = id };
            db.Taxes.Add(tax);
        }
        tax.Code = code;
        tax.Name = input.Name.Trim();
        tax.Rate = input.Rate;
        tax.IsActive = input.IsActive;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return Ok(tax);
    }

    [HttpPut("allergens/{id:guid}")]
    public async Task<IActionResult> SaveAllergen(
        Guid id,
        AllergenInput input,
        CancellationToken ct
    )
    {
        if (!CanManageGlobal())
            return Forbid();
        if (
            id == Guid.Empty
            || !ValidCode(input.Code)
            || !ValidName(input.Name)
            || input.Description is null
            || input.Description.Trim().Length > 500
        )
            return BadRequest("Código, nombre o descripción de alérgeno inválido.");
        string code = input.Code.Trim().ToUpperInvariant();
        if (await db.Allergens.AnyAsync(x => x.Code == code && x.Id != id, ct))
            return Conflict("El código de alérgeno ya existe.");
        CatalogAllergen? allergen = await db.Allergens.FindAsync([id], ct);
        if (allergen is null)
        {
            allergen = new CatalogAllergen { Id = id };
            db.Allergens.Add(allergen);
        }
        allergen.Code = code;
        allergen.Name = input.Name.Trim();
        allergen.Description = input.Description.Trim();
        allergen.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(allergen);
    }

    [HttpPut("products/{productId:guid}/tax")]
    public async Task<IActionResult> SaveProductTax(
        Guid productId,
        [FromBody] Guid taxId,
        CancellationToken ct
    )
    {
        if (!CanManageGlobal())
            return Forbid();
        Product? product = await db.Products.FindAsync([productId], ct);
        if (product is null)
            return NotFound();
        if (taxId != Guid.Empty && !await db.Taxes.AnyAsync(x => x.Id == taxId && x.IsActive, ct))
            return BadRequest("El impuesto no existe o está inactivo.");
        product.AssignTax(taxId == Guid.Empty ? null : taxId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("products/{productId:guid}/allergens")]
    public async Task<IActionResult> SaveProductAllergens(
        Guid productId,
        List<Guid> allergenIds,
        CancellationToken ct
    )
    {
        if (!CanManageGlobal())
            return Forbid();
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct))
            return NotFound();
        Guid[] ids = allergenIds.Distinct().ToArray();
        if (await db.Allergens.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Length)
            return BadRequest("Hay alérgenos inexistentes o inactivos.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.ProductAllergens.Where(x => x.ProductId == productId).ExecuteDeleteAsync(ct);
        db.ProductAllergens.AddRange(
            ids.Select(id => new ProductAllergen { ProductId = productId, AllergenId = id })
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("products/{productId:guid}/definition")]
    public async Task<IActionResult> SaveDefinition(
        Guid productId,
        DefinitionInput input,
        CancellationToken ct
    )
    {
        if (!CanManageGlobal())
            return Forbid();
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct))
            return NotFound();
        if (input.IsCombo && !input.Groups.Any(g => g.Kind == "Choice"))
            return BadRequest("Un combo necesita al menos un grupo de elección.");
        if (
            (!input.IsCombo && input.AllowsExtras)
            || (input.IsCombo && !input.AllowsExtras && input.Groups.Any(g => g.Kind == "Extra"))
        )
            return BadRequest("Activa 'Permite extras' para añadir extras a un combo.");
        if (
            input.Groups.Any(g =>
                !ValidName(g.Name)
                || g.Kind is not ("Choice" or "Extra")
                || g.MinSelections < 0
                || g.MaxSelections < 1
                || g.MinSelections > g.MaxSelections
                || g.MaxSelections > g.ProductIds.Distinct().Count()
                || g.ProductIds.Contains(productId)
                || (g.Kind == "Choice" && (g.MinSelections != 1 || g.MaxSelections != 1))
                || (!input.IsCombo && g.Kind == "Choice")
            )
        )
            return BadRequest("Configuración de grupos inválida.");
        Guid[] optionIds = input.Groups.SelectMany(g => g.ProductIds).Distinct().ToArray();
        if (await db.Products.CountAsync(x => optionIds.Contains(x.Id), ct) != optionIds.Length)
            return BadRequest("Alguna opción no existe.");
        Guid[] extraIds = input
            .Groups.Where(x => x.Kind == "Extra")
            .SelectMany(x => x.ProductIds)
            .Distinct()
            .ToArray();
        if (
            await db.Products.CountAsync(
                x =>
                    extraIds.Contains(x.Id)
                    && x.TaxId != null
                    && db.Taxes.Any(t => t.Id == x.TaxId && t.IsActive),
                ct
            ) != extraIds.Length
        )
            return BadRequest("Todos los extras necesitan un impuesto.");
        if (
            await db.Prices.CountAsync(
                x =>
                    extraIds.Contains(x.ProductId)
                    && x.RestaurantId == Guid.Empty
                    && x.Channel == "LOCAL"
                    && x.Format == "RACION",
                ct
            ) != extraIds.Length
        )
            return BadRequest(
                "Todos los extras necesitan un precio predeterminado de formato RACION."
            );
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ProductConfiguration? configuration = await db.ProductConfigurations.FindAsync(
            [productId],
            ct
        );
        if (configuration is null)
            db.ProductConfigurations.Add(
                new ProductConfiguration
                {
                    ProductId = productId,
                    IsCombo = input.IsCombo,
                    AllowsExtras = input.AllowsExtras,
                }
            );
        else
        {
            configuration.IsCombo = input.IsCombo;
            configuration.AllowsExtras = input.AllowsExtras;
        }
        Guid[] oldGroupIds = await db
            .ProductOptionGroups.Where(x => x.ParentProductId == productId)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        await db.ProductOptions.Where(x => oldGroupIds.Contains(x.GroupId)).ExecuteDeleteAsync(ct);
        await db
            .ProductOptionGroups.Where(x => x.ParentProductId == productId)
            .ExecuteDeleteAsync(ct);
        foreach (var (group, order) in input.Groups.Select((g, i) => (g, i)))
        {
            Guid groupId = Guid.NewGuid();
            db.ProductOptionGroups.Add(
                new ProductOptionGroup
                {
                    Id = groupId,
                    ParentProductId = productId,
                    Name = group.Name.Trim(),
                    Kind = group.Kind,
                    MinSelections = group.MinSelections,
                    MaxSelections = group.MaxSelections,
                    SortOrder = order,
                }
            );
            db.ProductOptions.AddRange(
                group
                    .ProductIds.Distinct()
                    .Select(
                        (id, index) =>
                            new ProductOption
                            {
                                GroupId = groupId,
                                ProductId = id,
                                SortOrder = index,
                            }
                    )
            );
        }
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("products/{productId:guid}/prices/{restaurantId:guid}")]
    public async Task<IActionResult> SavePrices(
        Guid productId,
        Guid restaurantId,
        List<PriceInput> input,
        CancellationToken ct
    )
    {
        if (restaurantId != Guid.Empty || !CanManageGlobal())
            return Forbid();
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct))
            return NotFound();
        bool isExtra = await db
            .Products.Where(p => p.Id == productId)
            .Join(db.Categories, p => p.CategoryId, c => c.Id, (p, c) => c.Code)
            .AnyAsync(code => code == "EXTRAS" || code == "EXTRA", ct);
        if (
            input.Any(x =>
                !Channels.Contains(x.Channel)
                || !Formats.Contains(x.Format)
                || (isExtra && x.Format != "RACION")
                || x.Amount < 0
                || x.Amount > 9999999999m
            )
            || input.Select(x => (x.Channel, x.Format)).Distinct().Count() != input.Count
        )
            return BadRequest("Precios, formatos o canales inválidos o duplicados.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db
            .Prices.Where(x => x.ProductId == productId && x.RestaurantId == restaurantId)
            .ExecuteDeleteAsync(ct);
        db.Prices.AddRange(
            input.Select(x => new CatalogPrice
            {
                ProductId = productId,
                RestaurantId = restaurantId,
                Channel = x.Channel,
                Format = x.Format,
                Amount = x.Amount,
            })
        );
        CatalogMenu? defaultMenu = await db.CatalogMenus.FirstOrDefaultAsync(
            x => x.RestaurantId == Guid.Empty,
            ct
        );
        if (defaultMenu is null)
        {
            defaultMenu = new CatalogMenu
            {
                RestaurantId = Guid.Empty,
                Name = "Carta predeterminada",
            };
            db.CatalogMenus.Add(defaultMenu);
        }
        await db
            .CatalogMenuPrices.Where(x => x.MenuId == defaultMenu.Id && x.ProductId == productId)
            .ExecuteDeleteAsync(ct);
        db.CatalogMenuPrices.AddRange(
            input.Select(x => new CatalogMenuPrice
            {
                MenuId = defaultMenu.Id,
                ProductId = productId,
                Channel = x.Channel,
                Format = x.Format,
                Amount = x.Amount,
            })
        );
        CatalogMenuEntry? defaultEntry = await db.CatalogMenuEntries.FirstOrDefaultAsync(
            x => x.MenuId == defaultMenu.Id && x.ProductId == productId,
            ct
        );
        string? initialFormat = input
            .Where(x => x.Channel == "LOCAL")
            .OrderBy(x =>
                x.Format == "PLATO" ? 0
                : x.Format == "RACION" ? 1
                : 2
            )
            .Select(x => x.Format)
            .FirstOrDefault();
        if (initialFormat is not null)
        {
            if (defaultEntry is null)
            {
                defaultEntry = new CatalogMenuEntry
                {
                    MenuId = defaultMenu.Id,
                    ProductId = productId,
                    SortOrder = await db.CatalogMenuEntries.CountAsync(
                        x => x.MenuId == defaultMenu.Id,
                        ct
                    ),
                };
                db.CatalogMenuEntries.Add(defaultEntry);
            }
            if (!input.Any(x => x.Channel == "LOCAL" && x.Format == defaultEntry.DefaultFormat))
                defaultEntry.DefaultFormat = initialFormat;
        }
        else if (defaultEntry is not null)
            db.CatalogMenuEntries.Remove(defaultEntry);
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("menus/{menuId:guid}")]
    public async Task<IActionResult> SaveMenu(Guid menuId, MenuInput input, CancellationToken ct)
    {
        if (!CanManageScope(input.RestaurantId))
            return Forbid();
        if (
            !ValidName(input.Name)
            || input.Entries.Select(x => x.ProductId).Distinct().Count() != input.Entries.Count
            || input.Entries.Any(x =>
                !Formats.Contains(x.DefaultFormat)
                || x.Channels.Any(c => !Platforms.Contains(c.Channel) || c.ExternalId.Length > 160)
                || x.Channels.Select(c => c.Channel).Distinct().Count() != x.Channels.Count
            )
            || input.Prices.Any(x =>
                !Channels.Contains(x.Channel)
                || !Formats.Contains(x.Format)
                || x.Amount < 0
                || x.Amount > 9999999999m
            )
            || input.Prices.Select(x => (x.ProductId, x.Channel, x.Format)).Distinct().Count()
                != input.Prices.Count
            || input.Entries.Any(x =>
                x.IsAvailable
                && !input.Prices.Any(p =>
                    p.ProductId == x.ProductId
                    && p.Channel == "LOCAL"
                    && p.Format == x.DefaultFormat
                )
            )
        )
            return BadRequest("Carta o canales inválidos.");
        Guid[] productIds = input.Entries.Select(x => x.ProductId).ToArray();
        if (await db.Products.CountAsync(x => productIds.Contains(x.Id), ct) != productIds.Length)
            return BadRequest("La carta contiene productos inexistentes.");
        Guid[] pricedProductIds = input.Prices.Select(x => x.ProductId).Distinct().ToArray();
        if (
            await db.Products.CountAsync(x => pricedProductIds.Contains(x.Id), ct)
            != pricedProductIds.Length
        )
            return BadRequest("La carta contiene precios de productos inexistentes.");
        Guid[] extraGroupIds = await db
            .ProductOptionGroups.AsNoTracking()
            .Where(x => x.Kind == "Extra" && productIds.Contains(x.ParentProductId))
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        Guid[] allowedExtraIds = await db
            .ProductOptions.AsNoTracking()
            .Where(x => extraGroupIds.Contains(x.GroupId))
            .Select(x => x.ProductId)
            .Distinct()
            .ToArrayAsync(ct);
        if (pricedProductIds.Except(productIds).Except(allowedExtraIds).Any())
            return BadRequest(
                "La carta no puede asociar extras; solo cambiar el precio de los predeterminados."
            );
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        CatalogMenu? menu =
            menuId == Guid.Empty
                ? null
                : await db.CatalogMenus.SingleOrDefaultAsync(x => x.Id == menuId, ct);
        if (menuId != Guid.Empty && menu is null)
            return NotFound();
        if (menu is not null && menu.RestaurantId != input.RestaurantId)
            return BadRequest("Ámbito inválido.");
        if (menu is null)
        {
            if (
                input.RestaurantId == Guid.Empty
                && await db.CatalogMenus.AnyAsync(x => x.RestaurantId == Guid.Empty, ct)
            )
                return BadRequest("Ya existe una carta predeterminada.");
            menu = new CatalogMenu { RestaurantId = input.RestaurantId };
            db.CatalogMenus.Add(menu);
        }
        menu.Name = input.Name.Trim();
        await db.CatalogMenuPrices.Where(x => x.MenuId == menu.Id).ExecuteDeleteAsync(ct);
        Guid[] oldEntryIds = await db
            .CatalogMenuEntries.Where(x => x.MenuId == menu.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        await db
            .CatalogMenuChannels.Where(x => oldEntryIds.Contains(x.MenuEntryId))
            .ExecuteDeleteAsync(ct);
        await db.CatalogMenuEntries.Where(x => x.MenuId == menu.Id).ExecuteDeleteAsync(ct);
        foreach (var (entry, order) in input.Entries.Select((e, i) => (e, i)))
        {
            Guid entryId = Guid.NewGuid();
            db.CatalogMenuEntries.Add(
                new CatalogMenuEntry
                {
                    Id = entryId,
                    MenuId = menu.Id,
                    ProductId = entry.ProductId,
                    IsAvailable = entry.IsAvailable,
                    DefaultFormat = entry.DefaultFormat,
                    SortOrder = order,
                }
            );
            db.CatalogMenuChannels.AddRange(
                entry.Channels.Select(c => new CatalogMenuChannel
                {
                    MenuEntryId = entryId,
                    Channel = c.Channel,
                    IsPublished = c.IsPublished,
                    ExternalId = c.ExternalId.Trim(),
                })
            );
        }
        db.CatalogMenuPrices.AddRange(
            input.Prices.Select(x => new CatalogMenuPrice
            {
                MenuId = menu.Id,
                ProductId = x.ProductId,
                Channel = x.Channel,
                Format = x.Format,
                Amount = x.Amount,
            })
        );
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(input.RestaurantId, ct);
        await transaction.CommitAsync(ct);
        return Ok(menu);
    }

    [HttpPut("menus/{menuId:guid}/active")]
    public async Task<IActionResult> ActivateMenu(Guid menuId, CancellationToken ct)
    {
        CatalogMenu? menu = await db.CatalogMenus.SingleOrDefaultAsync(x => x.Id == menuId, ct);
        if (menu is null)
            return NotFound();
        if (!CanManageScope(menu.RestaurantId))
            return Forbid();
        CatalogMenuEntry[] activeEntries = await db
            .CatalogMenuEntries.AsNoTracking()
            .Where(x => x.MenuId == menuId && x.IsAvailable)
            .ToArrayAsync(ct);
        if (activeEntries.Length == 0)
            return BadRequest("La carta necesita productos disponibles.");
        Guid[] activeProductIds = activeEntries.Select(x => x.ProductId).ToArray();
        if (
            await db.Products.CountAsync(
                x =>
                    activeProductIds.Contains(x.Id)
                    && x.TaxId != null
                    && db.Taxes.Any(t => t.Id == x.TaxId && t.IsActive),
                ct
            ) != activeProductIds.Length
        )
            return BadRequest("Todos los productos disponibles necesitan un impuesto.");
        Guid[] activeExtraGroupIds = await db
            .ProductOptionGroups.AsNoTracking()
            .Where(x => x.Kind == "Extra" && activeProductIds.Contains(x.ParentProductId))
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        Guid[] activeExtraIds = await db
            .ProductOptions.AsNoTracking()
            .Where(x => activeExtraGroupIds.Contains(x.GroupId))
            .Select(x => x.ProductId)
            .Distinct()
            .ToArrayAsync(ct);
        if (
            await db.Products.CountAsync(
                x =>
                    activeExtraIds.Contains(x.Id)
                    && x.TaxId != null
                    && db.Taxes.Any(t => t.Id == x.TaxId && t.IsActive),
                ct
            ) != activeExtraIds.Length
        )
            return BadRequest("Todos los extras necesitan un impuesto.");
        CatalogMenuPrice[] localPrices = await db
            .CatalogMenuPrices.AsNoTracking()
            .Where(x => x.MenuId == menuId && x.Channel == "LOCAL")
            .ToArrayAsync(ct);
        if (
            activeEntries.Any(x =>
                !localPrices.Any(p => p.ProductId == x.ProductId && p.Format == x.DefaultFormat)
            )
        )
            return BadRequest("Cada producto necesita el precio de su formato inicial.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (menu.RestaurantId == Guid.Empty)
            await db
                .CatalogMenus.Where(x => x.Id != menuId && x.IsActive)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.IsActive, false), ct);
        else
        {
            await db
                .CatalogMenus.Where(x =>
                    x.Id != menuId && x.RestaurantId == menu.RestaurantId && x.IsActive
                )
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.IsActive, false), ct);
            await db
                .CatalogMenus.Where(x => x.RestaurantId == Guid.Empty && x.IsActive)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.IsActive, false), ct);
        }
        menu.IsActive = true;
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(menu.RestaurantId, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("menus/{menuId:guid}/inactive")]
    public async Task<IActionResult> DeactivateMenu(Guid menuId, CancellationToken ct)
    {
        CatalogMenu? menu = await db.CatalogMenus.SingleOrDefaultAsync(x => x.Id == menuId, ct);
        if (menu is null)
            return NotFound();
        if (!CanManageScope(menu.RestaurantId))
            return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        menu.IsActive = false;
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(menu.RestaurantId, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpDelete("menus/{menuId:guid}")]
    public async Task<IActionResult> DeleteLocalMenu(Guid menuId, CancellationToken ct)
    {
        CatalogMenu? menu = await db.CatalogMenus.SingleOrDefaultAsync(x => x.Id == menuId, ct);
        if (menu is null)
            return NotFound();
        if (menu.RestaurantId == Guid.Empty)
            return BadRequest("La carta general no se elimina.");
        if (!CanManageScope(menu.RestaurantId))
            return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.CatalogMenus.Remove(menu);
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(menu.RestaurantId, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    private static bool ValidCode(string? code) =>
        code is { Length: > 0 and <= 40 }
        && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;

    public sealed record TaxInput(string Code, string Name, decimal Rate, bool IsActive);

    public sealed record AllergenInput(string Code, string Name, string Description, bool IsActive);

    public sealed record DefinitionInput(bool IsCombo, bool AllowsExtras, List<GroupInput> Groups);

    public sealed record GroupInput(
        string Name,
        string Kind,
        int MinSelections,
        int MaxSelections,
        List<Guid> ProductIds
    );

    public sealed record PriceInput(string Channel, string Format, decimal Amount);

    public sealed record MenuInput(
        Guid RestaurantId,
        string Name,
        List<MenuEntryInput> Entries,
        List<MenuPriceInput> Prices
    );

    public sealed record MenuPriceInput(
        Guid ProductId,
        string Channel,
        string Format,
        decimal Amount
    );

    public sealed record MenuEntryInput(
        Guid ProductId,
        bool IsAvailable,
        string DefaultFormat,
        List<MenuChannelInput> Channels
    );

    public sealed record MenuChannelInput(string Channel, bool IsPublished, string ExternalId);
}
