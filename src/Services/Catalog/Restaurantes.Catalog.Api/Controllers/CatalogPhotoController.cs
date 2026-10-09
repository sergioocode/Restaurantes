using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Infrastructure.Media;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Controllers;

[ApiController, Route("api/catalog/products/{productId:guid}/photos")]
public sealed class CatalogPhotoController(CatalogWriteDbContext db, IProductImageStorage storage)
    : ControllerBase
{
    [HttpPost, Authorize, RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Upload(
        Guid productId,
        [FromForm] IFormFile original,
        CancellationToken ct
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.CatalogGlobalManage))
            return Forbid();
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct))
            return NotFound();
        if (original.Length is < 1 or > 10_000_000)
            return BadRequest("La imagen debe ocupar entre 1 byte y 10 MB.");

        byte[] originalBytes = await ReadAsync(original, ct);
        ProcessedProductImage processed;
        try
        {
            processed = ProductImageProcessor.Process(originalBytes);
        }
        catch (ProductImageValidationException error)
        {
            return BadRequest(error.Message);
        }

        Guid revision = Guid.NewGuid();
        try
        {
            await storage.StoreAsync(
                productId,
                revision,
                processed.OriginalExtension,
                processed.OriginalContentType,
                originalBytes,
                processed.Variants,
                ct
            );
            ProductPhoto? photo = await db.ProductPhotos.FindAsync([productId], ct);
            Guid oldRevision = photo?.Revision ?? Guid.Empty;
            if (photo is null)
            {
                photo = new ProductPhoto { ProductId = productId };
                db.ProductPhotos.Add(photo);
            }
            photo.Revision = revision;
            photo.OriginalContentType = processed.OriginalContentType;
            await db.SaveChangesAsync(ct);
            if (oldRevision != Guid.Empty)
            {
                try
                {
                    await storage.DeleteAsync(productId, oldRevision, ct);
                }
                catch (Exception)
                {
                    // La limpieza de una revisión antigua no invalida la nueva publicación.
                }
            }
            return Ok(new { productId, revision });
        }
        catch
        {
            await storage.DeleteAsync(productId, revision, CancellationToken.None);
            throw;
        }
    }

    [HttpGet("{variant}"), AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, string variant, CancellationToken ct)
    {
        variant = variant switch
        {
            "small" => "thumbnail.webp",
            "medium" => "card.webp",
            "large" => "detail.webp",
            "thumbnail" => "thumbnail.webp",
            "card" => "card.webp",
            "detail" => "detail.webp",
            _ => "",
        };
        if (variant.Length == 0)
            return NotFound();
        ProductPhoto? photo = await db
            .ProductPhotos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == productId, ct);
        if (photo is null)
            return NotFound();
        StoredProductImage? image = await storage.OpenReadAsync(
            productId,
            photo.Revision,
            variant,
            ct
        );
        if (image is null)
            return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image.Content, image.ContentType, enableRangeProcessing: true);
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        await using Stream input = file.OpenReadStream();
        using MemoryStream output = new();
        await input.CopyToAsync(output, ct);
        return output.ToArray();
    }
}
