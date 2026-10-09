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

        Assert.Equal("png", result.OriginalExtension);
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

    private static (int Width, int Height) Dimensions(byte[] bytes)
    {
        using SKBitmap image = SKBitmap.Decode(bytes)!;
        return (image.Width, image.Height);
    }
}
