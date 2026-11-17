// Mirrors: Rust reader.rs:150 public_ip (loopback, private, link-local, CGNAT, unique-local, multicast, unspecified, v4-mapped are not public);
// Fable review 1 (remote content must not reach the user's own network). Stub from stage F of M2; owner reader.

using System.Net;
using Barbican.Core.Common;

namespace Barbican.Core.Net;

/// <summary>Whether an address is on the public internet.</summary>
public static class PublicAddress
{
    /// <summary>False for loopback, private, link-local, CGNAT (100.64/10), multicast, unspecified, reserved, unique-local and v4-mapped non-public addresses.</summary>
    public static bool IsPublic(IPAddress address)
    {
        throw new PortPendingException("reader.rs:150 public_ip");
    }
}
