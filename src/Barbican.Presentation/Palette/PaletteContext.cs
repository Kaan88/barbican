// Mirrors: Rust palette.rs:70 Ctx (what the palette offers depends on where it was opened). Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Palette;

/// <summary>Where the palette was opened and what is true there.</summary>
public sealed record PaletteContext
{
    /// <summary>The open app: 0 Mail, 1 Calendar, 2 Contacts, 3 Files (Rust `app`).</summary>
    public int App { get; init; }

    public bool SettingsOpen { get; init; }

    public string SettingsTab { get; init; } = string.Empty;

    /// <summary>The settings pages as (id, label).</summary>
    public IReadOnlyList<(string Id, string Label)> SettingsTabs { get; init; } = [];

    public bool HasMail { get; init; }

    public bool MailUnread { get; init; }

    public bool MailFlagged { get; init; }

    public bool InJunk { get; init; }

    public int CalView { get; init; }

    public bool InTrash { get; init; }

    public bool Calendars { get; init; }

    public bool Contacts { get; init; }

    public bool Files { get; init; }

    public bool Admin { get; init; }

    public bool Dark { get; init; }

    public bool SingleKeys { get; init; }

    public IReadOnlyList<string> Accounts { get; init; } = [];

    public string Account { get; init; } = string.Empty;
}
