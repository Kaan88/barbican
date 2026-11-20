using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Invokes a hook only while the UI resource and its account are current at the actual native post.</summary>
internal interface IPluginFencedHookRuntime
{
    Task<Result<PluginValue>> InvokeHookWhileCurrentAsync(string hookName, PluginValue arguments,
        TimeSpan budget, Func<Func<bool>, bool> runWhileCurrent, CancellationToken ct);

    Task<Result<PluginValue>> InvokeCheckerAsync(PluginCheckerRegistration spec, PluginCheckerRegistry registry,
        string accountKey, PluginValue arguments, Func<Func<bool>, bool> runWhileCurrent, CancellationToken ct);
}
