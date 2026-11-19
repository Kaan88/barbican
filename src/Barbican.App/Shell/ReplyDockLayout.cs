// Mirrors Bulwark components/mail/reply-dock.tsx:14-25, 40-70.
// Native reader has a 200 px minimum and does not expose the web reader's chrome/body measurements.
namespace Barbican.App.Shell;

/// <summary>The initial dock allocation within the native reader column.</summary>
public readonly record struct ReplyDockLayout(bool Takeover, double Height)
{
    public const double ShareMinPane = 700;
    public const double MinDock = 320;
    public const double MinNativeReader = 200;
    public const double DefaultShare = 0.6;

    public static ReplyDockLayout Compute(double paneHeight)
    {
        if (!double.IsFinite(paneHeight) || paneHeight <= 0)
        {
            return new ReplyDockLayout(false, 0);
        }

        if (paneHeight < ShareMinPane || paneHeight - MinNativeReader < MinDock)
        {
            return new ReplyDockLayout(true, paneHeight);
        }

        var desired = Math.Round(paneHeight * DefaultShare, MidpointRounding.AwayFromZero);
        return new ReplyDockLayout(false, Math.Clamp(desired, MinDock, paneHeight - MinNativeReader));
    }
}
