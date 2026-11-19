// Mirrors: nothing in Bulwark (a stand-in): the hooks of a plugin context created outside the host (tests). Owner unified (M2 wave 2).

namespace Barbican.Plugins;

/// <summary>Accepts subscriptions and never calls them (a context made outside the host).</summary>
public sealed class NoPluginHooks : IPluginHooks
{
    /// <summary>The one instance.</summary>
    public static readonly NoPluginHooks Instance = new();

    private NoPluginHooks()
    {
    }

    /// <inheritdoc />
    public IDisposable Observe(string hook, Func<HookEvent, CancellationToken, Task> handler) => Nothing.Instance;

    /// <inheritdoc />
    public IDisposable Intercept(string hook, Func<HookEvent, CancellationToken, Task<bool>> handler) => Nothing.Instance;

    /// <inheritdoc />
    public IDisposable Transform(string hook, Func<HookEvent, CancellationToken, Task<string?>> handler, int order = 100) => Nothing.Instance;

    /// <inheritdoc />
    public IDisposable TakeOver(string hook, Func<HookEvent, CancellationToken, Task<PluginTakeOverDecision>> handler, int order = 100) => Nothing.Instance;
    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
