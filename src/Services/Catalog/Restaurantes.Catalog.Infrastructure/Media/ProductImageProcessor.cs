using SkiaSharp;

namespace Restaurantes.Catalog.Infrastructure.Media;

public sealed class ProductImageValidationException(string message) : Exception(message);

public sealed record ProcessedProductImage(
    string OriginalExtension,
    string OriginalContentType,
    IReadOnlyList<ProductImageVariant> Variants
);

public static class ProductImageProcessor
{
    private const int MaxFileBytes = 10_000_000;
    private const long MaxPixels = 40_000_000;
    private static readonly (string Name, int MaxEdge)[] Sizes =
    [
        ("thumbnail.webp", 160),
        ("card.webp", 640),
        ("detail.webp", 1600),
    ];

    public static ProcessedProductImage Process(byte[] original)
    {
        if (original.Length is < 1 or > MaxFileBytes)
            throw new ProductImageValidationException(
                "La imagen debe ocupar entre 1 byte y 10 MB."
            );

        (string extension, string contentType) = DetectFormat(original);
        using SKBitmap? source = SKBitmap.Decode(original);
        if (source is null || source.Width < 1 || source.Height < 1)
            throw new ProductImageValidationException("El archivo no contiene una imagen válida.");
        if ((long)source.Width * source.Height > MaxPixels)
            throw new ProductImageValidationException(
                "La imagen supera el límite de 40 megapíxeles."
            );

        List<ProductImageVariant> variants = [];
        foreach ((string name, int maxEdge) in Sizes)
        {
            int width;
            int height;
            if (source.Width >= source.Height)
            {
                width = Math.Min(maxEdge, source.Width);
                height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
            }
            else
            {
                height = Math.Min(maxEdge, source.Height);
                width = Math.Max(1, (int)Math.Round(source.Width * (double)height / source.Height));
            }

            using SKBitmap resized =
                source.Resize(
                    new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                )
                ?? throw new ProductImageValidationException("No se pudo redimensionar la imagen.");
            using SKData encoded =
                resized.Encode(SKEncodedImageFormat.Webp, 82)
                ?? throw new ProductImageValidationException("No se pudo codificar la imagen.");
            variants.Add(new ProductImageVariant(name, encoded.ToArray(), "image/webp"));
        }

        return new ProcessedProductImage(extension, contentType, variants);
    }

    private static (string Extension, string ContentType) DetectFormat(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return ("jpg", "image/jpeg");
        if (StartsWith(bytes, [137, 80, 78, 71, 13, 10, 26, 10]))
            return ("png", "image/png");
        if (
            bytes.Length >= 12
            && StartsWith(bytes, "RIFF"u8.ToArray())
            && StartsWith(bytes[8..], "WEBP"u8.ToArray())
        )
            return ("webp", "image/webp");
        throw new ProductImageValidationException("Solo se admiten imágenes JPEG, PNG o WebP.");
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;
        for (int index = 0; index < prefix.Length; index++)
            if (bytes[index] != prefix[index])
                return false;
        return true;
    }
}
