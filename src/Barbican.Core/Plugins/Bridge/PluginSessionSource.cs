// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30 and host-api-composer.ts:29-32.
// Production account manager captures/checks under its own lock; no event-derived or permanently pinned active account.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Bridge;

public sealed class PluginSessionSource(IAccountSessionSnapshots sessions) : IPluginSessionSource
{
    public Result<PluginSessionCapture> CaptureActive() => Convert(sessions.CaptureActiveSession());
    public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Convert(sessions.CaptureOwnerSession(localAccountKey));
    public bool IsCurrent(PluginSessionCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return sessions.IsSessionCurrent(new(capture.Session, capture.ActiveSelectionGeneration));
    }
    private static Result<PluginSessionCapture> Convert(Result<AccountSessionSnapshot> result) => result.Map(snapshot => new PluginSessionCapture(snapshot.Session, snapshot.ActiveSelectionGeneration));
}
