// Test helper: an independent CRC-32 (IEEE 802.3) so the zip and PNG tests do not check the production code against itself.

namespace Barbican.Core.Tests.Mail.Avatars;

internal static class TestCrc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Hash(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}
