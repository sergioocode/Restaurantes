using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Api.Realtime;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Controllers;

[ApiController, Authorize, Route("api/catalog/design")]
public sealed class CatalogDesignController(
    CatalogWriteDbContext db,
    CatalogMenuSynchronizer synchronizer,
    CatalogNotifications notifications
) : ControllerBase
{
    private const string LocalChannel = "LOCAL";
    private const string PortionFormat = "RACION";
    private const string ChoiceGroupKind = "Choice";
    private const string ExtraGroupKind = "Extra";
    private static readonly HashSet<string> Formats =
    [
        "PLATO",
        PortionFormat,
        "TAPA",
        "MEDIA_RACION",
        "UNIDAD",
        "COPA",
        "TERCIO",
        "VASO",
        "EXTRA",
        "COMBO",
    ];
    private static readonly HashSet<string> Channels = [LocalChannel, "DELIVERY", "GLOVO", "UBER"];

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
        RestaurantProductAvailability[] localAvailability = await db
            .RestaurantProductAvailabilities.AsNoTracking()
            .ToArrayAsync(ct);
        return Ok(
            new
            {
                products = await db
                    .Products.AsNoTracking()
                    .Select(x => new
                    {
                        x.Id,
                        x.Sku,
                        x.Name,
                        x.CategoryId,
                        x.IsActive,
                        x.Version,
                        x.UpdatedAtUtc,
                    })
                    .ToArrayAsync(ct),
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
                localAvailability = localAvailability.Where(x => CanManageScope(x.RestaurantId)),
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
        try
        {
            await db.SaveChangesAsync(ct);
            await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(
                "El producto cambió durante el guardado. Recarga e inténtalo de nuevo."
            );
        }
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
        string? validationError = await ValidateDefinitionAsync(productId, input, ct);
        if (validationError is not null)
            return BadRequest(validationError);

        await PersistDefinitionAsync(productId, input, ct);
        return NoContent();
    }

    private async Task<string?> ValidateDefinitionAsync(
        Guid productId,
        DefinitionInput input,
        CancellationToken ct
    )
    {
        if (input.IsCombo && !input.Groups.Any(g => g.Kind == ChoiceGroupKind))
            return "Un combo necesita al menos un grupo de elección.";
        if (HasInvalidExtraConfiguration(input))
            return "Activa 'Permite extras' para añadir extras a un combo.";
        if (input.Groups.Any(group => !IsValidDefinitionGroup(productId, input.IsCombo, group)))
            return "Configuración de grupos inválida.";

        Guid[] optionIds = input.Groups.SelectMany(g => g.ProductIds).Distinct().ToArray();
        if (await db.Products.CountAsync(x => optionIds.Contains(x.Id), ct) != optionIds.Length)
            return "Alguna opción no existe.";
        Guid[] extraIds = input
            .Groups.Where(x => x.Kind == ExtraGroupKind)
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
            return "Todos los extras necesitan un impuesto.";
        if (
            await db.Prices.CountAsync(
                x =>
                    extraIds.Contains(x.ProductId)
                    && x.RestaurantId == Guid.Empty
                    && x.Channel == LocalChannel
                    && x.Format == PortionFormat,
                ct
            ) != extraIds.Length
        )
            return "Todos los extras necesitan un precio predeterminado de formato RACION.";

        return null;
    }

    private static bool HasInvalidExtraConfiguration(DefinitionInput input)
    {
        return (!input.IsCombo && input.AllowsExtras)
            || (
                input.IsCombo
                && !input.AllowsExtras
                && input.Groups.Any(group => group.Kind == ExtraGroupKind)
            );
    }

    private static bool IsValidDefinitionGroup(Guid productId, bool isCombo, GroupInput group)
    {
        return ValidName(group.Name)
            && group.Kind is ChoiceGroupKind or ExtraGroupKind
            && group.MinSelections >= 0
            && group.MaxSelections >= 1
            && group.MinSelections <= group.MaxSelections
            && group.MaxSelections <= group.ProductIds.Distinct().Count()
            && !group.ProductIds.Contains(productId)
            && (
                group.Kind != ChoiceGroupKind
                || group.MinSelections == 1 && group.MaxSelections == 1
            )
            && (isCombo || group.Kind != ChoiceGroupKind);
    }

    private async Task PersistDefinitionAsync(
        Guid productId,
        DefinitionInput input,
        CancellationToken ct
    )
    {
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
        if (HasInvalidPrices(input, isExtra))
            return BadRequest("Precios, formatos o canales inválidos o duplicados.");

        await PersistPricesAsync(productId, restaurantId, input, ct);
        return NoContent();
    }

    private static bool HasInvalidPrices(List<PriceInput> input, bool isExtra)
    {
        return input.Any(price =>
                !Channels.Contains(price.Channel)
                || !Formats.Contains(price.Format)
                || (isExtra && price.Format != PortionFormat)
                || price.Amount < 0
                || price.Amount > 9999999999m
            )
            || input.Select(price => (price.Channel, price.Format)).Distinct().Count()
                != input.Count;
    }

    private async Task PersistPricesAsync(
        Guid productId,
        Guid restaurantId,
        List<PriceInput> input,
        CancellationToken ct
    )
    {
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
        await UpdateDefaultMenuAsync(productId, input, ct);
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(Guid.Empty, ct);
        await transaction.CommitAsync(ct);
        await notifications.MenuChanged(Guid.Empty);
    }

    private async Task UpdateDefaultMenuAsync(
        Guid productId,
        List<PriceInput> input,
        CancellationToken ct
    )
    {
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
            .Where(price => price.Channel == LocalChannel)
            .OrderBy(price => GetInitialFormatPriority(price.Format))
            .Select(price => price.Format)
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
            if (
                !input.Any(price =>
                    price.Channel == LocalChannel && price.Format == defaultEntry.DefaultFormat
                )
            )
                defaultEntry.DefaultFormat = initialFormat;
        }
        else if (defaultEntry is not null)
            db.CatalogMenuEntries.Remove(defaultEntry);
    }

    private static int GetInitialFormatPriority(string format) =>
        format switch
        {
            "PLATO" => 0,
            PortionFormat => 1,
            _ => 2,
        };

    [HttpPut("restaurants/{restaurantId:guid}/products/{productId:guid}/availability")]
    public async Task<IActionResult> SetLocalAvailability(
        Guid restaurantId,
        Guid productId,
        [FromBody] bool available,
        CancellationToken ct
    )
    {
        if (restaurantId == Guid.Empty || !CanManageScope(restaurantId))
            return Forbid();
        CatalogMenu? menu = await db
            .CatalogMenus.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == Guid.Empty && x.IsActive, ct);
        Product? product = await db
            .Products.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (menu is null || product is null)
            return BadRequest("El producto no existe en el catálogo global.");
        if (available && !product.IsActive)
            return BadRequest("El producto está desactivado en el catálogo global.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        RestaurantProductAvailability? selection =
            await db.RestaurantProductAvailabilities.SingleOrDefaultAsync(
                x => x.RestaurantId == restaurantId && x.ProductId == productId,
                ct
            );
        if (selection is null)
            db.RestaurantProductAvailabilities.Add(
                new RestaurantProductAvailability
                {
                    RestaurantId = restaurantId,
                    ProductId = productId,
                    IsAvailable = available,
                }
            );
        else
            selection.IsAvailable = available;
        await db.SaveChangesAsync(ct);
        await synchronizer.SyncAffectedMenus(restaurantId, ct);
        await transaction.CommitAsync(ct);
        await notifications.MenuChanged(restaurantId);
        return NoContent();
    }

    private static bool ValidCode(string? code) =>
        code is { Length: > 0 and <= 40 }
        && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;

    public sealed record TaxInput(
        string Code,
        string Name,
        [property: JsonRequired] decimal Rate,
        [property: JsonRequired] bool IsActive
    );

    public sealed record AllergenInput(
        string Code,
        string Name,
        string Description,
        [property: JsonRequired] bool IsActive
    );

    public sealed record DefinitionInput(
        [property: JsonRequired] bool IsCombo,
        [property: JsonRequired] bool AllowsExtras,
        List<GroupInput> Groups
    );

    public sealed record GroupInput(
        string Name,
        string Kind,
        [property: JsonRequired] int MinSelections,
        [property: JsonRequired] int MaxSelections,
        List<Guid> ProductIds
    );

    public sealed record PriceInput(
        string Channel,
        string Format,
        [property: JsonRequired] decimal Amount
    );
}
