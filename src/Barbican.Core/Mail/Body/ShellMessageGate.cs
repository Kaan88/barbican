// Mirrors: Rust mailview.rs:326 on_ipc, which silently ignored unknown messages; here they are also
//   counted, so tests and the automation build can see a page sending garbage (docs/adr/0005 "M0 review").
// Bulwark: none.
using System.Diagnostics.CodeAnalysis;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// The host's entry point for page → host messages: validates through <see cref="ShellMessage.TryParse"/>
/// and counts what it drops. One per shell host.
/// </summary>
public sealed class ShellMessageGate
{
    private long _dropped;

    /// <summary>Messages rejected so far (unknown, malformed, oversized, or a link with a refused scheme).</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryAccept(string? raw, [NotNullWhen(true)] out ShellMessage? message)
    {
        if (ShellMessage.TryParse(raw, out message))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }
}
