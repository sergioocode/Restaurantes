using Restaurantes.Catalog.Infrastructure.Media;
using SkiaSharp;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class ProductImageProcessorTests
{
    [Fact]
    public void Process_creates_named_webp_variants_without_upscaling()
    {
        using SKBitmap source = new(2000, 1000);
        using SKData encoded = source.Encode(SKEncodedImageFormat.Png, 100);

        ProcessedProductImage result = ProductImageProcessor.Process(encoded.ToArray());

        Assert.Equal("image/png", result.OriginalContentType);
        Assert.Equal(
            ["thumbnail.webp", "card.webp", "detail.webp"],
            result.Variants.Select(x => x.Name)
        );
        Assert.Equal((160, 80), Dimensions(result.Variants[0].Content));
        Assert.Equal((640, 320), Dimensions(result.Variants[1].Content));
        Assert.Equal((1600, 800), Dimensions(result.Variants[2].Content));
    }

    [Fact]
    public void Process_rejects_an_unsupported_file()
    {
        Assert.Throws<ProductImageValidationException>(() =>
            ProductImageProcessor.Process("not-image"u8.ToArray())
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7_000_001)]
    public void Process_rejects_files_outside_the_size_limit(int size)
    {
        ProductImageValidationException error = Assert.Throws<ProductImageValidationException>(() =>
            ProductImageProcessor.Process(new byte[size])
        );

        Assert.Equal("La imagen debe ocupar entre 1 byte y 7 MB.", error.Message);
    }

    [Fact]
    public void Process_accepts_an_image_at_the_size_limit()
    {
        using SKBitmap source = new(2, 2);
        using SKData encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        byte[] original = new byte[7_000_000];
        // El relleno tras la imagen permite comprobar el límite exacto de bytes.
        encoded.ToArray().CopyTo(original, 0);

        ProcessedProductImage result = ProductImageProcessor.Process(original);

        Assert.Equal("image/png", result.OriginalContentType);
        Assert.Equal(3, result.Variants.Count);
        Assert.All(result.Variants, variant => Assert.Equal((2, 2), Dimensions(variant.Content)));
    }

    private static (int Width, int Height) Dimensions(byte[] bytes)
    {
        using SKBitmap image = SKBitmap.Decode(bytes)!;
        return (image.Width, image.Height);
    }
}
