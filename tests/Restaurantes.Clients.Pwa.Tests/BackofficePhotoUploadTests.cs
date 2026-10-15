using System.Net;
using Microsoft.AspNetCore.Components.Forms;
using Restaurantes.Clients.Backoffice.Pwa.Api;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class BackofficePhotoUploadTests
{
    [Fact]
    public async Task Oversized_photo_is_rejected_before_reading_or_sending_it()
    {
        RecordingHandler handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://test.local") };
        BackofficeApi api = new(http);
        BrowserPhoto photo = new(7_000_001);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.UploadProductPhotoAsync(Guid.NewGuid(), photo)
        );

        Assert.Equal("La foto supera 7 MB.", error.Message);
        Assert.False(photo.Opened);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Photo_at_the_limit_is_sent_as_original_with_the_correct_limit()
    {
        RecordingHandler handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://test.local") };
        BackofficeApi api = new(http) { AccessToken = "test-token" };
        BrowserPhoto photo = new(7_000_000);
        Guid productId = Guid.NewGuid();

        await api.UploadProductPhotoAsync(productId, photo);

        Assert.True(photo.Opened);
        Assert.Equal(7_000_000, photo.ReadLimit);
        Assert.Equal(1, handler.Requests);
        Assert.Equal($"/api/catalog/products/{productId}/photos", handler.Path);
        Assert.Equal("original", handler.FieldName);
        Assert.Equal(7_000_000, handler.Bytes);
        Assert.Equal("image/png", handler.ContentType);
    }

    private sealed class BrowserPhoto(long size) : IBrowserFile
    {
        public string Name => "photo.png";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => size;
        public string ContentType => "image/png";
        public bool Opened { get; private set; }
        public long ReadLimit { get; private set; }

        public Stream OpenReadStream(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default
        )
        {
            Opened = true;
            ReadLimit = maxAllowedSize;
            if (Size > maxAllowedSize)
                throw new IOException("File exceeds the requested stream limit.");
            return new MemoryStream(new byte[checked((int)Size)]);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? Path { get; private set; }
        public string? FieldName { get; private set; }
        public string? ContentType { get; private set; }
        public int Bytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Path = request.RequestUri!.AbsolutePath;
            MultipartFormDataContent form = Assert.IsType<MultipartFormDataContent>(
                request.Content
            );
            HttpContent photo = Assert.Single(form);
            FieldName = photo.Headers.ContentDisposition!.Name!.Trim('"');
            ContentType = photo.Headers.ContentType!.MediaType;
            Bytes = (await photo.ReadAsByteArrayAsync(cancellationToken)).Length;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
