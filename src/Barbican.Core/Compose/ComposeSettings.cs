// Mirrors: Rust compose.rs (reads at :169, :307, :333-339, :405, :426, :798-809, :868-879), settings.rs; Bulwark settings-store keys sendDelaySeconds, autoSelectReplyIdentity, replyIdentityMatch, replyUnderMessage, signatureNewMailEnabled,
// signatureReplyForwardEnabled, signaturePosition, signatureSeparatorEnabled, plainTextMode, attachmentReminderEnabled, emptySubjectWarningEnabled, placeholderWarningEnabled, sendConfirmation, subAddressDelimiter, writingLanguage, quickReplyEnabled
// (spec/settings.csv). Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Storage;

namespace Barbican.Core.Compose;

/// <summary>The composing settings, read from the prefs when a composer opens (the defaults for anything not stored).</summary>
public sealed record ComposeSettings
{
    /// <summary>Undo window in seconds (0, 5, 10, 20, 30 in the settings page).</summary>
    public int SendDelaySeconds { get; init; } = 5;

    public bool AutoSelectReplyIdentity { get; init; }

    /// <summary>"exact" or "domain".</summary>
    public string ReplyIdentityMatch { get; init; } = "domain";

    public bool ReplyUnderMessage { get; init; } = true;

    public bool SignatureNewMail { get; init; } = true;

    public bool SignatureReplyForward { get; init; } = true;

    /// <summary>"below_quote" or "above_quote".</summary>
    public string SignaturePosition { get; init; } = "below_quote";

    public bool SignatureSeparator { get; init; } = true;

    public bool PlainTextMode { get; init; }

    public bool AttachmentReminder { get; init; } = true;

    public bool EmptySubjectWarning { get; init; } = true;

    public bool PlaceholderWarning { get; init; } = true;

    public bool SendConfirmation { get; init; }

    /// <summary>The sub-address delimiter ("+" by default); empty disables sub-address stripping.</summary>
    public string SubAddressDelimiter { get; init; } = "+";

    /// <summary>"auto", a short code (en, de, fr, es, tr) or a BCP 47 tag.</summary>
    public string WritingLanguage { get; init; } = "auto";

    public bool QuickReply { get; init; } = true;

    public static ComposeSettings Read(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new ComposeSettings
        {
            SendDelaySeconds = (int)Math.Clamp(prefs.Number("sendDelaySeconds", 5), 0, 3600),
            AutoSelectReplyIdentity = prefs.Bool("autoSelectReplyIdentity", false),
            ReplyIdentityMatch = prefs.Str("replyIdentityMatch", "domain"),
            ReplyUnderMessage = prefs.Bool("replyUnderMessage", true),
            SignatureNewMail = prefs.Bool("signatureNewMailEnabled", true),
            SignatureReplyForward = prefs.Bool("signatureReplyForwardEnabled", true),
            SignaturePosition = prefs.Str("signaturePosition", "below_quote"),
            SignatureSeparator = prefs.Bool("signatureSeparatorEnabled", true),
            PlainTextMode = prefs.Bool("plainTextMode", false),
            AttachmentReminder = prefs.Bool("attachmentReminderEnabled", true),
            EmptySubjectWarning = prefs.Bool("emptySubjectWarningEnabled", true),
            PlaceholderWarning = prefs.Bool("placeholderWarningEnabled", true),
            SendConfirmation = prefs.Bool("sendConfirmation", false),
            SubAddressDelimiter = prefs.Str("subAddressDelimiter", "+"),
            WritingLanguage = prefs.Str("writingLanguage", "auto"),
            QuickReply = prefs.Bool("quickReplyEnabled", true),
        };
    }
}
