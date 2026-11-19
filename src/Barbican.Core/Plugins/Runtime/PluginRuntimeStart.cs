// Mirrors: Bulwark protocol.ts init payloads; bundle code is read from the validated package, never supplied as arbitrary host state.
using System.Text.Json;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Core.Plugins.Runtime;
public sealed class PluginRuntimeStart(PluginInstallation installation, PluginGrantSnapshot grant, string profile,
    PluginFrameKind kind, string? slot, JsonElement props, PluginHostState hostState)
{
    public PluginInstallation Installation { get; } = installation;
    public PluginGrantSnapshot Grant { get; } = grant;
    public string Profile { get; } = profile;
    public PluginFrameKind Kind { get; } = kind;
    public string? Slot { get; } = slot;
    public JsonElement Props { get; } = props.Clone();
    public PluginHostState HostState { get; } = hostState;
}
