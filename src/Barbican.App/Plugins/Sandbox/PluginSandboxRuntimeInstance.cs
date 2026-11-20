using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

internal sealed class PluginSandboxRuntimeInstance(PluginSandboxSession session, PluginActivation activation) : IPluginRuntimeInstance
{
    public PluginRuntimeBinding Binding => session.Binding;
    public PluginActivation Activation { get; } = activation;

    public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Activation.Hooks.Contains(hookName, StringComparer.Ordinal)
            ? session.InvokeHookAsync(hookName, arguments, budget, ct)
            : UnavailableAsync<PluginValue>(ct);
    }

    public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) =>
        UnavailableAsync<PluginValue>(ct);

    public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => UnavailableAsync<bool>(ct);

    public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => UnavailableAsync<Unit>(ct);

    public ValueTask DisposeAsync() => session.DisposeAsync();

    private static Task<Result<T>> UnavailableAsync<T>(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<T>.Fail(new ValidationError("The add-on operation is unavailable.")));
    }
}
