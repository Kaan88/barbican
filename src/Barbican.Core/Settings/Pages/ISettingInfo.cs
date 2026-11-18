// Mirrors: Rust settings.rs (info rows of the schema), Bulwark components/settings/about-data-settings.tsx, account-settings.tsx (the read-only rows: version, storage used, account addresses, protocol handlers). Contract from the M6 plan section 4.2; owner set-data.
// THREADING: TextAsync completes off the UI thread (it may call the server); the screen marshals the answer.

namespace Barbican.Core.Settings.Pages;

/// <summary>The text of the keyless info rows (each keyless row gets a handler or an info provider, PORT-PLAN M6).</summary>
public interface ISettingInfo
{
    /// <summary>Whether this provider answers for the row (its S-keyless id).</summary>
    bool Provides(string rowId);

    /// <summary>The current text of an info row (the app version, "3.5 MB of 1 GB used", the signed-in address); empty when there is nothing to say.</summary>
    Task<string> TextAsync(string rowId, CancellationToken ct);
}
