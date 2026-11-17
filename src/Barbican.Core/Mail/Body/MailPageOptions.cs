// Mirrors: Rust main.rs:229 page() inputs (DARK, emailAlwaysLightMode, a11y::A11y, plainTextFont, messageSpacing, the images switch;
// tag rust-final); Bulwark components/email/email-viewer.tsx (emailAlwaysLightMode, plainTextFont, messageSpacing). Owner body.

using Barbican.Core.Settings.Accessibility;

namespace Barbican.Core.Mail.Body;

/// <summary>What shapes a message page besides the message.</summary>
public sealed record MailPageOptions
{
    /// <summary>The app is drawn dark.</summary>
    public bool Dark { get; init; }

    /// <summary>"Always show emails in light mode" (<c>emailAlwaysLightMode</c>).</summary>
    public bool AlwaysLight { get; init; }

    /// <summary>The accessibility settings (spacing, width, links, motion, text size, the colour override).</summary>
    public A11y A11y { get; init; } = new();

    /// <summary><c>plainTextFont</c>: <c>sans</c> (default) or <c>mono</c> for text bodies.</summary>
    public string PlainTextFont { get; init; } = "sans";

    /// <summary><c>messageSpacing</c>: <c>auto</c> (no gutter for full-bleed layouts), <c>edge</c> (never), <c>always</c>.</summary>
    public string MessageSpacing { get; init; } = "auto";

    /// <summary>Remote images, stylesheets and fonts may load (the per-message "Load external content").</summary>
    public bool AllowRemote { get; init; }

    /// <summary>HTML with its own colours gets the dark conversion (email_dark.js) when the app is dark.</summary>
    public bool Recolor { get; init; } = true;

    /// <summary>The reader's colour (the tile token <c>Reader</c>, CSS hex): themed pages and the empty view take it.</summary>
    public string ReaderBackground { get; init; } = "#141414";
}
