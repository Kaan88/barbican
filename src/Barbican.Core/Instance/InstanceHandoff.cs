// Mirrors: Rust instance.rs:24 mailto_arg, :46 send, :74 read_request, :89 serve; Rust instance.rs (tag rust-final); PORT-PLAN.md section 3.9 (single instance, mailto handoff). Contract stub from the M3 plan section 4.5; owner composer-ui replaces the bodies.

using System.Net.Sockets;
using Barbican.Core.Common;

namespace Barbican.Core.Instance;

/// <summary>
/// The mailto handoff between launches over a loopback TCP socket (one mechanism on both OSes, D-241): a token line and a URL line, the server greets first, at most 8 connections at once, a short deadline per connection,
/// 256 KiB per request. Stub until stream composer-ui lands.
/// </summary>
public static class InstanceHandoff
{
    /// <summary>The first command-line argument that is a mailto: link (as the OS passes it, quoted "%1").</summary>
    public static string? MailtoArg(IReadOnlyList<string> args) => throw new PortPendingException("InstanceHandoff.MailtoArg");

    /// <summary>One handoff from a connection: the URL when the token matches and the line is a mailto: link, else null (a foreign scheme such as file: is refused).</summary>
    public static string? ReadRequest(Stream stream, string token) => throw new PortPendingException("InstanceHandoff.ReadRequest");

    /// <summary>Hands <paramref name="url"/> to the instance at <paramref name="endpoint"/>: connects, checks the server's greeting, sends the token and the URL. False when the greeting is wrong (a foreign program on a stale port gets nothing) or the token is refused.</summary>
    public static bool Send(HandoffEndpoint endpoint, string url) => throw new PortPendingException("InstanceHandoff.Send");

    /// <summary>Serves handoffs from later launches until <paramref name="ct"/> is cancelled: each valid link goes to <paramref name="onLink"/>. Idle connections do not hold up a real handoff.</summary>
    public static Task ServeAsync(TcpListener listener, string token, string greet, Action<string> onLink, CancellationToken ct) => throw new PortPendingException("InstanceHandoff.ServeAsync");
}
