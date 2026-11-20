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
#if AUTOMATION
    internal static Action? BeforeSyntheticCheckerPost { get; set; }
    internal static Action<PluginValue>? AfterSyntheticCheckerPost { get; set; }
#endif
    internal async Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget,
        CancellationToken ct, Func<Func<bool>, bool>? runWhileCurrent = null, string? proofreadingAccount = null,
        Func<Func<bool>, bool>? whileRegistered = null)
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
            if (wire is null || !await View.PostAsync((write, postToken) => new(PostInvocationAsync(delivery, id, wire,
                write, arguments, runWhileCurrent, proofreadingAccount, whileRegistered, postToken)), token).ConfigureAwait(false))
                return HookUnavailable();

            var reply = await pending.WaitAsync(token).ConfigureAwait(false);
            if (!await (proofreadingAccount is null ? delivery.TryPostAsync(Binding, _ => CanReturnHook(token), token)
                : delivery.TryPostProofreadingAsync(Binding, proofreadingAccount, () => CanReturnHook(token), token)).ConfigureAwait(false))
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

    private Task<bool> PostInvocationAsync(IPluginDeliveryAuthority delivery, string id, string wire, Action<string> write,
        PluginValue arguments, Func<Func<bool>, bool>? runWhileCurrent, string? proofreadingAccount,
        Func<Func<bool>, bool>? whileRegistered, CancellationToken ct)
    {
        bool NativePost()
        {
            var posted = PostHook(id, wire, write, whileRegistered, ct);
#if AUTOMATION
            if (posted && proofreadingAccount is not null) AfterSyntheticCheckerPost?.Invoke(arguments);
#endif
            return posted;
        }
        bool Post() => runWhileCurrent is null ? NativePost() : runWhileCurrent(NativePost);
#if AUTOMATION
        if (proofreadingAccount is not null) BeforeSyntheticCheckerPost?.Invoke();
#endif
        return proofreadingAccount is null ? delivery.TryPostAsync(Binding, _ => Post(), ct)
            : delivery.TryPostProofreadingAsync(Binding, proofreadingAccount, Post, ct);
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

    private bool PostHook(string id, string wire, Action<string> write, Func<Func<bool>, bool>? whileRegistered, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!Active || ct.IsCancellationRequested) return false;
            var posted = false;
            return Binding.TryWriteWhileAlive(() =>
            {
                posted = whileRegistered is null ? TryWrite() : whileRegistered(TryWrite);

                bool TryWrite()
                {
                    if (!_hookReplies.TryMarkPosted(id)) return false;
                    write(wire);
                    return true;
                }
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
