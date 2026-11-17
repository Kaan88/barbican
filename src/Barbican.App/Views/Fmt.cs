// Mirrors: Rust app rows (tint = RowText.TintIndex, rose/sky/mint/amber as design/README.md); design/mockups BNMail "tints". x:Bind functions the views share.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.UI.Text;
using Windows.UI;
using Windows.UI.Text;

namespace Barbican.App.Views;

/// <summary>Small pure functions used from x:Bind expressions (colours come from the live palette brushes, so a theme flip needs no rebinding).</summary>
public static class Fmt
{
    private static readonly string[] TintBgKeys = ["RoseBg", "SkyBg", "MintBg", "AmberBg"];
    private static readonly string[] TintFgKeys = ["RoseFg", "SkyFg", "MintFg", "AmberFg"];

    public static Brush TintBg(int index) => Palette(TintBgKeys[Math.Abs(index) % 4]);

    public static Brush TintFg(int index) => Palette(TintFgKeys[Math.Abs(index) % 4]);

    public static Brush Palette(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>A tag colour 0xRRGGBB as a brush (tag colours are the user's, not the palette's).</summary>
    public static Brush Rgb(uint rgb) => new SolidColorBrush(Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    public static bool Not(bool value) => !value;

    public static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisNot(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The names a list row's chips show: the first two, then "+N" (spec I-129).</summary>
    public static IReadOnlyList<string> Chips(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count <= 2 ? names : [names[0], names[1], $"+{names.Count - 2}"];
    }

    public static Visibility VisClip(bool hasAttachment, int chipCount) => hasAttachment && chipCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisChips(int chipCount) => chipCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Brush UnreadDot(bool unread) => unread ? Palette("Accent") : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public static Visibility VisCount(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisMany(int count) => count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisBody(BodyState state, string which) => which switch
    {
        "empty" => state == BodyState.Empty ? Visibility.Visible : Visibility.Collapsed,
        "loading" => state == BodyState.Loading ? Visibility.Visible : Visibility.Collapsed,
        "failed" => state == BodyState.Failed ? Visibility.Visible : Visibility.Collapsed,
        "truncated" => state == BodyState.Truncated ? Visibility.Visible : Visibility.Collapsed,
        "content" => state == BodyState.Empty ? Visibility.Collapsed : Visibility.Visible,
        _ => Visibility.Collapsed,
    };

    public static FontWeight Weight(bool unread) => unread ? FontWeights.Bold : FontWeights.Medium;

    public static FontWeight FolderWeight(int unread) => unread > 0 ? FontWeights.SemiBold : FontWeights.Normal;

    public static string ChevronId(string folderId) => folderId + ".chevron";

    public static double IndentPx(int depth) => depth * 14;

    public static Brush TimeBrush(bool unread) => Palette(unread ? "Accent" : "Faint");

    public static GridLength Star(double fraction) => new(Math.Clamp(fraction, 0, 1), GridUnitType.Star);

    public static GridLength StarRest(double fraction) => new(1 - Math.Clamp(fraction, 0, 1), GridUnitType.Star);

    public static Visibility VisStorage(double fraction) => fraction >= 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Thickness Indent(int depth) => new(depth * 14, 0, 0, 0);

    public static string FolderIcon(MailboxRole role) => role switch
    {
        MailboxRole.Inbox => "Inbox",
        MailboxRole.Drafts => "File",
        MailboxRole.Sent => "Send",
        MailboxRole.Trash => "Trash",
        MailboxRole.Junk => "Alert",
        MailboxRole.Archive => "Archive",
        MailboxRole.Flagged => "Star",
        MailboxRole.Important => "Alert",
        MailboxRole.All => "Archive",
        _ => "Folder",
    };

    public static string Chevron(int state) => state == 2 ? "ChevronRight" : "ChevronDown";

    public static Visibility VisChevron(int state) => state == 0 ? Visibility.Collapsed : Visibility.Visible;

    public static double MutedOpacity(bool muted) => muted ? 0.55 : 1;

    public static Brush FolderText(bool unread) => Palette(unread ? "Text" : "Body");

    public static string Count(int n) => n > 0 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    public static string NewCount(int unread) => unread > 0 ? $"{unread} new" : string.Empty;

    public static string Thread(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Conversation(int count) => $"Conversation · {count} messages";

    public static string TodayValue(bool hasCalendar, int count) => !hasCalendar ? "No calendar" : count switch { 0 => "No meetings", 1 => "1 meeting", _ => $"{count} meetings" };

    public static string FlaggedValue(bool has, int count) => !has || count == 0 ? "Nothing flagged" : count == 1 ? "1 message" : $"{count} messages";

    public static string TasksValue(bool hasTasks, int count) => !hasTasks ? "No tasks" : count switch { 0 => "All clear", 1 => "1 task", _ => $"{count} tasks" };

    public static Brush RailBackground(AppId active, string app) => Palette(string.Equals(active.ToString(), app, StringComparison.Ordinal) ? "Sel" : "Hover");

    public static Brush RailForeground(AppId active, string app) => Palette(string.Equals(active.ToString(), app, StringComparison.Ordinal) ? "SelText" : "Faint");

    public static Visibility VisBadge(string badge) => string.IsNullOrEmpty(badge) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush AuthBackground(string result) => Palette(result switch { "pass" => "MintBg", "fail" or "softfail" or "permerror" => "RoseBg", _ => "AmberBg" });

    public static Brush AuthForeground(string result) => Palette(result switch { "pass" => "MintFg", "fail" or "softfail" or "permerror" => "RoseFg", _ => "AmberFg" });

    public static string AuthText(string method, string result) => $"{(method ?? string.Empty).ToUpperInvariant()} {result}";

    /// <summary>" to Name, Name" after the sender, empty without recipients.</summary>
    public static string ToLine(string to) => string.IsNullOrEmpty(to) ? string.Empty : $" to {to}";

    public static string DateLine(string date) => string.IsNullOrEmpty(date) ? string.Empty : $" · {date}";

    public static string Angle(string email) => string.IsNullOrEmpty(email) ? string.Empty : $"<{email}>";

    public static Brush CurrentBackground(bool current) => current ? Palette("Hover") : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public static string AttachmentId(int index) => $"reader.attachment.{index}";

    public static string FailedText(string? error) => string.IsNullOrEmpty(error) ? "This message could not be loaded." : $"This message could not be loaded: {error}";

    public static string ReplyHint(string from) => string.IsNullOrEmpty(from) ? "Write a reply…" : $"Reply to {from}…";

    public static string AuthLabel(AuthVerdict verdict) => verdict switch
    {
        AuthVerdict.Verified => "Verified sender",
        AuthVerdict.Failed => "Sender not verified",
        _ => "No authentication result",
    };

    public static Brush VerdictBackground(AuthVerdict verdict) => Palette(verdict switch { AuthVerdict.Verified => "MintBg", AuthVerdict.Failed => "RoseBg", _ => "AmberBg" });

    public static Brush VerdictForeground(AuthVerdict verdict) => Palette(verdict switch { AuthVerdict.Verified => "MintFg", AuthVerdict.Failed => "RoseFg", _ => "AmberFg" });

    public static string Badge(string kind) => string.IsNullOrEmpty(kind) ? "FILE" : kind.Length > 4 ? kind[..4].ToUpperInvariant() : kind.ToUpperInvariant();

    public static string ExpandText(bool expanded) => expanded ? "Hide details" : "Details";

    public static string ChevronOf(bool expanded) => expanded ? "ChevronDown" : "ChevronRight";
}
