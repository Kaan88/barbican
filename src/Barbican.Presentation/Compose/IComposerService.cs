// Mirrors: M3 plan section 4.5; Rust compose.rs open :134, should_dock :418; Bulwark components/email/email-composer.tsx, lib/stores/ui-store.ts. Contract from the M3 plan; owner composer-ui implements, every stream that opens a composer consumes (reply/forward commands, the Scheduled view's "Cancel and edit", mailto).
// Additive members (composer-ui): Current, FlushAsync.

using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose;

/// <summary>Opens and closes the composer (inline in place of the reader, as Bulwark does).</summary>
public interface IComposerService
{
    /// <summary>A composer is open.</summary>
    bool IsOpen { get; }

    /// <summary>The open composer (the shell draws it), or null.</summary>
    ComposerViewModel? Current => null;

    event EventHandler? OpenChanged;

    Task<bool> SendInlineReplyAsync(ComposeRequest request, Func<ComposerDialog, CancellationToken, Task<string?>> ask, CancellationToken ct) => Task.FromResult(false);

    /// <summary>Prepares and opens a composer (the plugin's onBeforeCompose / onBeforeReply hooks run first and can stop it). Asks before replacing an open composer that holds unsaved work.</summary>
    Task OpenAsync(ComposeRequest request, CancellationToken ct);

    /// <summary>Opens a composer for a seed that is already prepared (undo send, cancel and edit).</summary>
    Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct);

    /// <summary>The leave guard (I-019, I-234): false when the user cancelled the "save or discard" question. A blank composer closes without asking.</summary>
    Task<bool> TryCloseAsync(CancellationToken ct);

    /// <summary>Quitting: waits for the save in flight and saves unsaved work silently, so closing the window loses nothing.</summary>
    Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
}
