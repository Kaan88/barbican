using System.Runtime.InteropServices;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Protocol.Messages;

namespace Barbican.App.Plugins.Sandbox;

public sealed partial class PluginSandboxSession
{
    internal async Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (arguments.Kind != PluginValueKind.Array || budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes(10) ||
            Authority is not IPluginDeliveryAuthority delivery)
            return HookUnavailable();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Binding.Lifetime);
        linked.CancelAfter(budget);
        var token = linked.Token;
        lock (_gate)
        {
            if (!Active || !_initializationSignal.Task.IsCompletedSuccessfully || _activationFailed)
                return HookUnavailable();
        }
        if (!_hookReplies.TryBegin(token, out var id, out var pending)) return HookUnavailable();
        try
        {
            var wire = EncodeHook(hookName, id, arguments);
            if (wire is null || !await View.PostAsync((write, postToken) =>
                new(delivery.TryPostAsync(Binding, _ => PostHook(id, wire, write, postToken), postToken)), token).ConfigureAwait(false))
                return HookUnavailable();

            var reply = await pending.WaitAsync(token).ConfigureAwait(false);
            if (!await delivery.TryPostAsync(Binding, _ => CanReturnHook(token), token).ConfigureAwait(false))
                return HookUnavailable();
            ct.ThrowIfCancellationRequested();
            return InterpretHookReply(reply);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return HookUnavailable();
        }
        catch (PluginValueFormatException)
        {
            return HookUnavailable();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return HookUnavailable();
        }
        finally
        {
            await linked.CancelAsync().ConfigureAwait(false);
        }
    }

    private static string? EncodeHook(string hookName, string id, PluginValue arguments)
    {
        var builder = new PluginValueBuilder();
        var message = builder.Build(builder.Object(("type", builder.String("hook-invoke")), ("id", builder.String(id)),
            ("hookName", builder.String(hookName)), ("args", builder.Import(arguments))), WireLimits);
        return PluginMessageValidator.Validate(message, PluginMessageDirection.HostToSandbox, MessageLimits)
            ? Encoding.UTF8.GetString(PluginValueCodec.Encode(message, WireLimits))
            : null;
    }

    private bool PostHook(string id, string wire, Action<string> write, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!Active || ct.IsCancellationRequested) return false;
            var posted = false;
            return Binding.TryWriteWhileAlive(() =>
            {
                if (!_hookReplies.TryMarkPosted(id)) return;
                write(wire);
                posted = true;
            }) && posted;
        }
    }

    private bool CanReturnHook(CancellationToken ct)
    {
        lock (_gate) return Active && !ct.IsCancellationRequested && Binding.TryWriteWhileAlive(static () => { });
    }

    private static Result<PluginValue> InterpretHookReply(PluginValue reply)
    {
        if (!reply.Property("ok").BooleanValue)
            return Result<PluginValue>.Fail(new ValidationError("The add-on hook failed."));
        if (reply.PropertyNames.Contains("result", StringComparer.Ordinal))
            return Result<PluginValue>.Ok(reply.Property("result"));
        var builder = new PluginValueBuilder();
        return Result<PluginValue>.Ok(builder.Build(builder.Undefined()));
    }

    private static Result<PluginValue> HookUnavailable() =>
        Result<PluginValue>.Fail(new TransportError("The add-on hook is unavailable or timed out.", Retryable: false));
}
