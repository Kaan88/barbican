// Mirrors: Rust mail.rs rebuild_folders :115 (colorfulSidebarIcons, showFolderTotalCount, A11y unread_style, colorBlindPalette). Owner mailcore.

using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Mail;

/// <summary>The settings that shape the folder rows.</summary>
public sealed record FolderRowOptions
{
    public bool Colorful { get; init; }

    public bool ShowTotals { get; init; } = true;

    /// <summary>count, dot or hidden.</summary>
    public string UnreadStyle { get; init; } = "count";

    public bool Cvd { get; init; }

    public static FolderRowOptions Read(PrefsSnapshot prefs, SystemAccessibility system)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var a = A11y.From(prefs, system);
        return new FolderRowOptions
        {
            Colorful = prefs.Bool("colorfulSidebarIcons", true),
            ShowTotals = prefs.Bool("showFolderTotalCount", true),
            UnreadStyle = a.UnreadStyle,
            Cvd = a.Cvd,
        };
    }
}
