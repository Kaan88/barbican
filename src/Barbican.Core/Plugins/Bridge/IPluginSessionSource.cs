// Mirrors: Bulwark active-session versus resource-owner resolution; shared JMAP ids are validated separately inside that captured login.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Bridge;
public interface IPluginSessionSource
{
    Result<PluginSessionCapture> CaptureActive();
    Result<PluginSessionCapture> CaptureOwner(string localAccountKey);
    bool IsCurrent(PluginSessionCapture capture);

    /// <summary>Runs a synchronous transport write under the session source's selection fence.</summary>
    bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) =>
        captures.Count == 0 && write();
}
