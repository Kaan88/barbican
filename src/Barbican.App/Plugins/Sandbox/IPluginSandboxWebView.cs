// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:327-477; native ownership is supplied by the WebView adapter, never a message field.
namespace Barbican.App.Plugins.Sandbox;
/// <summary>One isolated native WebView. RootIdentity is an opaque native-owned object, not a script value.</summary>
public interface IPluginSandboxWebView : IAsyncDisposable
{
    object RootIdentity { get; }
    Uri TrustedRoot { get; }
    Task NavigateAsync(Uri uri, CancellationToken ct);
    /// <summary>Runs delivery inside the native UI queue. The writer posts synchronously and may not enqueue again.</summary>
    Task<bool> PostAsync(Func<Action<ReadOnlyMemory<byte>>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct);
}
