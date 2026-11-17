// Fable review 1 F-09: the streaming PNG thumbnail gives exactly what decode-then-ThumbnailScaler.Fill gives, for every colour type and
// filter, larger and smaller than the thumbnail; a picture that claims more pixels than the caps is refused before anything is inflated.
// Owner mailcore (fix-security stream).

using Barbican.Core.Mail.Attachments;

namespace Barbican.Core.Tests.Mail.Attachments;

public sealed class PngThumbnailTests
{
    private static byte[] Scanlines(int width, int height, int channels, int seed, int paletteSize = 0)
    {
        var random = new Random(seed);
        var stride = width * channels;
        var data = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            data[row] = (byte)(y % 5); // every filter type, applied to random bytes
            for (var i = 1; i <= stride; i++)
            {
                data[row + i] = (byte)random.Next(256);
            }

            if (paletteSize > 0)
            {
                // palette indices stay unfiltered so they land inside the palette
                data[row] = 0;
                for (var i = 1; i <= stride; i++)
                {
                    data[row + i] = (byte)(data[row + i] % paletteSize);
                }
            }
        }

        return data;
    }

    [Theory]
    [InlineData(0, 1, 120, 60)]
    [InlineData(2, 3, 97, 203)]
    [InlineData(4, 2, 33, 33)]
    [InlineData(6, 4, 250, 181)]
    [InlineData(6, 4, 20, 7)]
    [InlineData(2, 3, 1, 1)]
    [InlineData(3, 1, 161, 90)]
    public void The_streaming_thumbnail_equals_decode_and_fill(byte colorType, int channels, int width, int height)
    {
        var palette = colorType == 3 ? Enumerable.Range(0, 16 * 3).Select(i => (byte)(i * 5)).ToArray() : null;
        var png = TestPng.Raw(width, height, colorType, Scanlines(width, height, channels, seed: width * height, paletteSize: palette is null ? 0 : 16), palette);
        var decoder = new PngDecoder();
        var whole = decoder.TryDecode(png);
        Assert.NotNull(whole);
        var expected = ThumbnailScaler.Fill(whole, AttachmentCards.ThumbnailEdge);
        var streamed = decoder.TryThumbnail(png, AttachmentCards.ThumbnailEdge);
        Assert.NotNull(streamed);
        Assert.Equal((expected.Width, expected.Height), (streamed.Width, streamed.Height));
        Assert.Equal(expected.Rgba, streamed.Rgba);
    }

    [Fact]
    public void A_picture_over_the_caps_is_refused_before_inflating()
    {
        // the header claims 20000 x 20000 (400 MP) and the data is a few bytes: nothing is allocated for the pixels
        var bomb = TestPng.Raw(20_000, 20_000, 6, new byte[16]);
        var decoder = new PngDecoder();
        Assert.Null(decoder.TryDecode(bomb));
        Assert.Null(decoder.TryThumbnail(bomb, 80));

        // 5000 x 5000 (25 MP): over the whole-picture cap, so only the streaming thumbnail reads it (and its data ends early: null, no throw)
        var large = TestPng.Raw(5_000, 5_000, 0, new byte[5_001 * 3]);
        Assert.Null(decoder.TryDecode(large));
        Assert.Null(decoder.TryThumbnail(large, 80));
        Assert.True(5_000L * 5_000 > PngDecoder.MaxDecodePixels);
    }

    [Fact]
    public void A_large_picture_streams_into_a_thumbnail_in_a_few_rows_of_memory()
    {
        // 6000 x 4000 grey (24 MP, over the whole-picture cap): the thumbnail path reads it and allocates far less than the picture
        const int W = 6000;
        const int H = 4000;
        var data = new byte[(W + 1) * H];
        for (var y = 0; y < H; y++)
        {
            data.AsSpan((y * (W + 1)) + 1, W).Fill((byte)(y * 255 / H));
        }

        var png = TestPng.Raw(W, H, 0, data);
        var decoder = new PngDecoder();
        _ = decoder.TryThumbnail(TestPng.Solid(4, 4, 1, 2, 3, 255), 80); // warm up
        var before = GC.GetAllocatedBytesForCurrentThread();
        var thumb = decoder.TryThumbnail(png, 80);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotNull(thumb);
        Assert.True(allocated < 8_000_000, $"allocated {allocated} bytes for a 24 MP picture");
        Assert.True(thumb.Rgba[0] < thumb.Rgba[((79 * 80) + 0) * 4], "top is darker than bottom");
    }

    [Fact]
    public void Attachment_thumbnails_use_the_streaming_path()
    {
        var png = TestPng.Raw(7_000, 3_000, 2, new byte[((7_000 * 3) + 1) * 3_000]);
        var t = AttachmentCards.Thumbnail(png);
        Assert.NotNull(t);
        Assert.Equal(AttachmentCards.ThumbnailEdge, t.Width);
    }
}
