namespace Barbican.Automation.Protocol;

/// <summary>
/// The fail-closed popup rule of AUTOMATION builds (docs/adr/0005 and 0007 "M0 review"): a popup that is
/// not constrained to the window is a separate top-level window, which Windows places on a real monitor
/// even when the app window is off-screen. Every such popup is closed at once and the command that
/// was running fails with <c>error unconstrained-popup TYPE</c>. UI-free; <c>PopupGuard</c> applies it.
/// </summary>
public static class PopupGuardPolicy
{
    public const string ErrorPrefix = "error unconstrained-popup ";

    /// <summary>The popups to close (every unconstrained one) and the error reply, or none.</summary>
    public static (IReadOnlyList<int> Close, string? Error) Evaluate(IReadOnlyList<OpenPopup> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var close = new List<int>();
        string? error = null;
        for (var i = 0; i < open.Count; i++)
        {
            if (!open[i].ConstrainedToRootBounds)
            {
                close.Add(i);
                error ??= ErrorPrefix + (string.IsNullOrWhiteSpace(open[i].ContentType) ? "Popup" : open[i].ContentType);
            }
        }

        return (close, error);
    }

    /// <summary>The reply after a command: the guard's error wins over the command's own reply.</summary>
    public static string Reply(string commandReply, string? guardError) => guardError ?? commandReply;
}
