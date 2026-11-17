// Mirrors: Bulwark lib/plugin-mail-hooks.ts (a failing handler is logged, never rethrown). Owner mailcore.

namespace Barbican.Core.Plugins.Hooks;

/// <summary>A hook handler threw.</summary>
public sealed class HookFailedEventArgs(string hook, Exception error) : EventArgs
{
    public string Hook { get; } = hook;

    public Exception Error { get; } = error;
}
