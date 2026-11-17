// Mirrors: Rust shortcuts.rs:40 Ctx (what the UI looks like right now, as far as the keys care). Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Input;

/// <summary>The UI state a key press is resolved against.</summary>
public sealed record ShortcutContext
{
    /// <summary>A message is open (Barbican's selection).</summary>
    public bool HasMail { get; init; }

    /// <summary>The calendar's event editor is open.</summary>
    public bool EditorOpen { get; init; }

    /// <summary>A contact is selected.</summary>
    public bool HasContact { get; init; }

    /// <summary>A file row is highlighted.</summary>
    public bool HasFile { get; init; }

    /// <summary>Settings, Accessibility, Single-key shortcuts.</summary>
    public bool SingleKeys { get; init; }

    /// <summary>A g was pressed less than the go-to sequence time ago (1500 ms, Bulwark GO_TO_SEQUENCE_MS).</summary>
    public bool GPending { get; init; }
}
