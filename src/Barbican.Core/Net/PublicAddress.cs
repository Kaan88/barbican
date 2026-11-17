// Mirrors: Rust reader.rs:150 public_ip (loopback, private, link-local, CGNAT, documentation, unspecified, broadcast, multicast, 0/8 and
// 240/4 for IPv4; loopback, unspecified, multicast, unique-local fc00::/7, link-local fe80::/10 and the v4-mapped form for IPv6); RFC 6890
// (special-purpose registries), RFC 6598 (100.64/10), RFC 4193, RFC 4291; Fable review 1 (remote content must not reach the user's own network).
// Stricter than Rust (an address is refused when any of these hold): IPv6 site-local fec0::/10, documentation 2001:db8::/32, and every form that
// embeds an IPv4 address (::a.b.c.d, NAT64 64:ff9b::/96, 6to4 2002::/16) is judged by the embedded IPv4 address. Review of the M2 reader
// stream: also IPv4 192.0.0.0/24 (IETF protocol assignments) and 198.18.0.0/15 (benchmarking), the local-use NAT64 prefix 64:ff9b:1::/48
// (RFC 8215: it leads into the operator's own network) and Teredo 2001::/32 (the client address is hidden in the address, so it cannot be judged).

using System.Net;
using System.Net.Sockets;

namespace Barbican.Core.Net;

/// <summary>Whether an address is on the public internet.</summary>
public static class PublicAddress
{
    /// <summary>False for loopback, private, link-local, CGNAT (100.64/10), multicast, unspecified, reserved, unique-local and v4-mapped non-public addresses.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsPublicV4(address.GetAddressBytes());
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicV4(address.MapToIPv4().GetAddressBytes());
        }

        var b = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        // multicast ff00::/8, unique-local fc00::/7, link-local fe80::/10, site-local fec0::/10, documentation 2001:db8::/32
        if (b[0] == 0xff || (b[0] & 0xfe) == 0xfc || (b[0] == 0xfe && (b[1] & 0xc0) is 0x80 or 0xc0) || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8))
        {
            return false;
        }

        // ::a.b.c.d (IPv4-compatible, deprecated) and every other address in ::/96
        if (b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0)
        {
            return false;
        }

        // local-use NAT64 64:ff9b:1::/48 (RFC 8215) and Teredo 2001:0::/32: refused outright
        if ((b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4] == 0x00 && b[5] == 0x01) || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00))
        {
            return false;
        }

        // NAT64 64:ff9b::/96 and 6to4 2002::/16 carry an IPv4 address the packet ends up at
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            return IsPublicV4(b[12..16]);
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4(b[2..6]);
        }

        return true;
    }

    private static bool IsPublicV4(byte[] o)
    {
        var loopback = o[0] == 127;
        var priv = o[0] == 10 || (o[0] == 172 && (o[1] & 0xf0) == 16) || (o[0] == 192 && o[1] == 168);
        var linkLocal = o[0] == 169 && o[1] == 254;
        var unspecifiedOrThisNet = o[0] == 0;
        var broadcast = o[0] == 255 && o[1] == 255 && o[2] == 255 && o[3] == 255;
        var multicast = (o[0] & 0xf0) == 224;
        var documentation = (o[0] == 192 && o[1] == 0 && o[2] == 2) || (o[0] == 198 && o[1] == 51 && o[2] == 100) || (o[0] == 203 && o[1] == 0 && o[2] == 113);
        var cgnat = o[0] == 100 && o[1] >= 64 && o[1] < 128;
        var reserved = o[0] >= 240;
        var protocolAssignments = o[0] == 192 && o[1] == 0 && o[2] == 0;
        var benchmarking = o[0] == 198 && (o[1] & 0xfe) == 18;
        return !(loopback || priv || linkLocal || unspecifiedOrThisNet || broadcast || multicast || documentation || cgnat || reserved || protocolAssignments || benchmarking);
    }
}
