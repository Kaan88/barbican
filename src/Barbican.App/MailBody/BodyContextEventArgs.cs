// Mirrors: Bulwark components/email/reader-selection-menu.tsx:95-99 (a right-click in the body: its point and the selected text). RD-08. Owner reader.

namespace Barbican.App.MailBody;

/// <summary>A right-click in the message body: the point in page coordinates and the text selected there (empty when none).</summary>
public sealed class BodyContextEventArgs(int x, int y, string selection) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public string Selection { get; } = selection;
}
