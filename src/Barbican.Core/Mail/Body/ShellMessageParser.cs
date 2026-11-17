// Mirrors: Rust mailview.rs:278 parse_key, :326 on_ipc (messages the mail view's shell page posts); PORT-PLAN.md §3.6. Stub from F2; owner body.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Body;

/// <summary>Reads the messages the mail view posts to the native side.</summary>
public static class ShellMessageParser
{
    /// <summary>Parses <c>flags|key</c> (flags among c, s, a); anything the page may not forward gives null.</summary>
    public static ForwardedKey? ParseKey(string spec) => throw new PortPendingException("ShellMessageParser.ParseKey: body stream");
}
