// Host-added native full-trust activation; Rust plugins.rs:64-107 and M6b lifecycle revocation.
using Barbican.Core.Common;
using Barbican.Plugins;
namespace Barbican.Core.Plugins.Native;

/// <summary>An activated native plugin and its revocation-owned lifetime.</summary>
public sealed class NativePluginActivation : IAsyncDisposable
{
    internal NativePluginActivation() { }
#pragma warning disable CA1822 // Test-first instance seam; an activation will own the validated manifest.
    public PluginManifest Manifest => throw new PortPendingException("M6b native consented activation");
#pragma warning restore CA1822
    public ValueTask DisposeAsync() => throw new PortPendingException("M6b native consented activation");
}
