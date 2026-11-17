#if AUTOMATION
// Mirrors: M2 plan 4.7 (`plugins`), 6.5 hidden scenario ("plugins lists spam-score"); spec/automation-protocol.md (M2 unified rows). Lists the
// native plugins the host loaded (waits for the start-up load), with state and hooks, and the load errors. Owner unified.

using Barbican.Automation;
using Barbican.Core.Plugins.Native;

namespace Barbican.App.Views.Unified.Automation;

/// <summary>The <c>plugins</c> command: <c>plugins N id[on|off,bundled,hook+hook] ... errors=E</c>.</summary>
public sealed class PluginsCommand(NativePlugins plugins) : IAutomationCommand
{
    public string Name => "plugins";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        _ = await plugins.LoadAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
        var items = plugins.Plugins.Select(p => $"{p.Id}[{(p.Enabled ? "on" : "off")}{(p.Bundled ? ",bundled" : string.Empty)},{string.Join("+", p.Hooks)}]");
        return $"plugins {plugins.Plugins.Count} {string.Join(" ", items)} errors={plugins.LoadErrors.Count}".Replace("  ", " ", StringComparison.Ordinal);
    }
}
#endif
