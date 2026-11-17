// Mirrors: Rust mail.rs:24 SyncStates, :34 states_of (kept only when the page says canCalculateChanges); RFC 8620 §5.5. Owner jmap.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Sync;

/// <summary>The states a list was read at, for a later delta.</summary>
/// <param name="Query">Email/query queryState.</param>
/// <param name="Email">Email state.</param>
/// <param name="Thread">Thread state.</param>
public sealed record ListStates(string Query, string Email, string Thread)
{
    /// <summary>The states of a page, or null when the server cannot calculate changes for it (Rust mail.rs:34 states_of).</summary>
    public static ListStates? Of(ListPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.CanCalculateChanges && page.QueryState.Length > 0 && page.EmailState.Length > 0 && page.ThreadState.Length > 0
            ? new ListStates(page.QueryState, page.EmailState, page.ThreadState)
            : null;
    }

    /// <summary>The states after a delta.</summary>
    public static ListStates After(ListDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        return new ListStates(delta.QueryState, delta.EmailState, delta.ThreadState);
    }
}
