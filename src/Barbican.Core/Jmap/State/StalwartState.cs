// Mirrors: Rust jmap.rs:1391 STALWART_BASE32, :1396 stalwart_change_id, :1419 stalwart_state, :1443 went_back;
// Bulwark lib/jmap/stalwart-state.ts:12-87 (base32Encode/Decode, leb128Encode/Decode, stalwartChangeId);
// Stalwart crates/jmap-proto/src/types/state.rs (State::Exact written as `s` + base32(LEB128(change id))). Owner jmap.

using System.Text;

namespace Barbican.Core.Jmap.State;

/// <summary>
/// Stalwart's state strings carry the change id (<c>s</c> plus base32 of the LEB128 bytes, Stalwart's own alphabet), so a
/// state that went backwards (a restore on the server) is detectable. <c>n</c> (initial) and <c>r…</c> (intermediate) carry none.
/// </summary>
public static class StalwartState
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz792013";

    // A u64 needs at most ten LEB128 bytes (Bulwark MAX_LEB128_BYTES).
    private const int MaxLeb128Bytes = 10;

    /// <summary>The state string Stalwart writes for a change id.</summary>
    public static string Encode(ulong changeId)
    {
        Span<byte> bytes = stackalloc byte[MaxLeb128Bytes];
        var n = 0;
        var id = changeId;
        while (true)
        {
            var b = (byte)(id & 0x7f);
            id >>= 7;
            if (id == 0)
            {
                bytes[n++] = b;
                break;
            }

            bytes[n++] = (byte)(b | 0x80);
        }

        var sb = new StringBuilder("s", 2 + (n * 8 / 5));
        uint buf = 0;
        var bits = 0;
        foreach (var b in bytes[..n])
        {
            buf = (buf << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(int)((buf >> bits) & 31)]);
            }

            buf &= (1u << bits) - 1;
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(int)((buf << (5 - bits)) & 31)]);
        }

        return sb.ToString();
    }

    /// <summary>The change id of a Stalwart state, or null for an opaque, initial, intermediate or non-canonical one.</summary>
    public static ulong? ChangeId(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length < 2 || state[0] != 's')
        {
            return null;
        }

        var bytes = new List<byte>(8);
        uint buf = 0;
        var bits = 0;
        foreach (var ch in state.AsSpan(1))
        {
            var v = Alphabet.IndexOf(ch, StringComparison.Ordinal);
            if (v < 0)
            {
                return null;
            }

            buf = (buf << 5) | (uint)v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buf >> bits) & 0xff));
                buf &= (1u << bits) - 1;
            }
        }

        ulong id = 0;
        for (var i = 0; i < bytes.Count && i < MaxLeb128Bytes; i++)
        {
            var shift = 7 * i;

            // Stalwart reads into a u64 and drops what does not fit (Bulwark BigInt.asUintN(64, …), Rust checked_shl).
            id |= shift < 64 ? (ulong)(bytes[i] & 0x7f) << shift : 0;
            if ((bytes[i] & 0x80) == 0)
            {
                // a non-canonical encoding (trailing bytes, padding) is not a state Stalwart wrote
                return string.Equals(Encode(id), state, StringComparison.Ordinal) ? id : null;
            }
        }

        return null;
    }

    /// <summary>True only when both are Stalwart states and the new one has a lower change id (its counters went back).</summary>
    public static bool WentBack(string since, string current) =>
        ChangeId(since) is { } a && ChangeId(current) is { } b && b < a;
}
