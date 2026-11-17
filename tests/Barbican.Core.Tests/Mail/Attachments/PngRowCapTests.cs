// Fable review 2 R2-08 (M2 plan §6.3 step 9): a PNG that claims 100,000,000 x 1 passed the pixel caps and asked for about 1.2 GB of row
// buffers. The header check now refuses a width over 32,768 and a row over 16 MB before any buffer is allocated.

using System.Buffers.Binary;
using Barbican.Core.Mail.Attachments;

namespace Barbican.Core.Tests.Mail.Attachments;

public sealed class PngRowCapTests
{
    [Theory]
    [InlineData(100_000_000, 1)]
    [InlineData(PngDecoder.MaxWidth + 1, 1)]
    [InlineData(1_000_000, 2)]
    public void A_picture_wider_than_the_cap_is_refused(int width, int height)
    {
        var png = WithSize(TestPng.Solid(1, 1, 1, 2, 3, 255), width, height);
        var decoder = new PngDecoder();
        Assert.Null(decoder.TryDecode(png));
        Assert.Null(decoder.TryThumbnail(png, 64));
    }

    [Fact]
    public void A_picture_at_the_cap_still_decodes()
    {
        var png = TestPng.Solid(PngDecoder.MaxWidth, 1, 9, 8, 7, 255);
        var thumb = new PngDecoder().TryThumbnail(png, 64);
        Assert.NotNull(thumb);
        Assert.True(PngDecoder.MaxWidth * 4L <= PngDecoder.MaxRowBytes);
    }

    // The same file with another size in IHDR (bytes 16-23); the decoder reads no CRC, and the refusal comes before the data.
    private static byte[] WithSize(byte[] png, int width, int height)
    {
        var copy = (byte[])png.Clone();
        BinaryPrimitives.WriteInt32BigEndian(copy.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(copy.AsSpan(20), height);
        return copy;
    }
}
