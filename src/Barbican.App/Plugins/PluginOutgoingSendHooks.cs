#if WINDOWS
// Mirrors Bulwark lib/outgoing/plugin-send-check.ts and lib/plugin-sandbox/hook-payload.ts.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Api.ComposeSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;

namespace Barbican.App.Plugins;

internal sealed class PluginOutgoingSendHooks(
    PluginBackgroundRuntimes runtimes, PluginPackageHost packages, IPluginSessionSource sessions,
    Func<ComposerViewModel?> currentComposer, IDispatcher dispatcher, PluginComposeTakeovers takeovers) : IOutgoingAddOnHooks
{
    public IOutgoingAddOnSend Start(IAccountSession sendingSession) => new Attempt(
        runtimes, packages, sessions, currentComposer, dispatcher, takeovers, new PluginSessionCapture(sendingSession, null));

    private sealed class Attempt(
        PluginBackgroundRuntimes runtimes, PluginPackageHost packages, IPluginSessionSource sessions,
        Func<ComposerViewModel?> currentComposer, IDispatcher dispatcher, PluginComposeTakeovers takeovers, PluginSessionCapture owner) : IOutgoingAddOnSend
    {
        private readonly IReadOnlyList<PluginSessionCapture> _captures = [owner];
        private readonly HashSet<PluginRuntimeBinding> _contributors = [];
        private (string? ComposerId, bool IsQuickReply, string AccountId)? _draft;
        private string? _sendRevision;

        public async Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct)
        {
            const string hook = HookNames.OnBeforeEmailSend;
            _draft ??= (outgoing.ComposerId, outgoing.IsQuickReply, outgoing.AccountId);
            await runtimes.WaitUntilInitializedAsync(ct).ConfigureAwait(false);
            if (!Owns(outgoing)) return false;
            PluginValue? payload = null;
            foreach (var runtime in Owners(hook))
            {
                payload ??= PluginOutgoingPayload.TryEncode(outgoing, beforeSend: true);
                if (runtime is not IPluginFencedHookRuntime fenced || payload is null) return false;
                var result = await fenced.InvokeHookWhileCurrentAsync(hook, payload,
                    HookPolicies.For(hook).Budget, write => RunWhileCurrent(runtime, write, ct), ct).ConfigureAwait(false);
                if (!result.IsOk || !Owns(outgoing) || result.Value is { Kind: PluginValueKind.Boolean, BooleanValue: false })
                    return false;
                _contributors.Add(runtime.Binding);
            }
            return Owns(outgoing);
        }

        public async Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct)
        {
            const string hook = HookNames.OnTransformOutgoingEmail;
            if (outgoing.IsQuickReply || !Owns(outgoing)) return outgoing;
            foreach (var runtime in Owners(hook))
            {
                if (runtime is not IPluginFencedHookRuntime fenced) continue;
                if (PluginOutgoingPayload.TryEncode(outgoing, beforeSend: false) is not { } payload) break;
                var result = await fenced.InvokeHookWhileCurrentAsync(hook, payload,
                    HookPolicies.For(hook).Budget, write => RunWhileCurrent(runtime, write, ct), ct).ConfigureAwait(false);
                if (!result.IsOk || result.Value is not { } answer || !Owns(outgoing) || !HasGrant(runtime, "email:send")) continue;
                var changed = PluginOutgoingPayload.Mediate(outgoing, answer, HasGrant(runtime, "email:write"));
                if (ReferenceEquals(changed, outgoing)) continue;
                _contributors.Add(runtime.Binding);
                outgoing = changed;
            }
            return outgoing;
        }

        public Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct) => TryIssueAsync(run => run(), issue, ct);

        public async Task<bool> TryIssueAsync(Func<Func<bool>, bool> guard, Func<bool> issue, CancellationToken ct)
        {
            var accepted = false;
            bool Issue() => guard(() => sessions.TryRunWhileCurrent(_captures, () =>
                !ct.IsCancellationRequested && DraftIsCurrent() && PluginRuntimeBinding.TryWriteWhileAllAlive(_contributors, issue)));
            await dispatcher.InvokeAsync(async () =>
            {
                accepted = _contributors.Count == 0
                    ? Issue()
                    : await packages.TryRunWithGrantsAsync(_contributors, Issue, ct).ConfigureAwait(true);
            }).ConfigureAwait(false);
            return accepted;
        }

        public async Task<Result<RawMessageReceipt?>> TakeOverAsync(ComposerTakeoverRequest request, CancellationToken ct)
        {
            const string hook = HookNames.OnComposeSend;
            _sendRevision = request.Revision;
            foreach (var runtime in Owners(hook))
            {
                if (runtime is not IPluginFencedHookRuntime fenced) return TakeoverFailed();
                var result = await InvokeTakeoverAsync(runtime, fenced, request, ct).ConfigureAwait(false);
                if (!result.IsOk || result.Value is not null) return result;
                _contributors.Add(runtime.Binding);
            }
            return Result<RawMessageReceipt?>.Ok(null);
        }

        private async Task<Result<RawMessageReceipt?>> InvokeTakeoverAsync(IPluginRuntimeInstance runtime,
            IPluginFencedHookRuntime fenced, ComposerTakeoverRequest request, CancellationToken ct)
        {
            const string hook = HookNames.OnComposeSend;
            bool IsCurrent() => HasGrant(runtime, "email:send") &&
                currentComposer() is { IsSending: true } draft &&
                string.Equals(draft.Id, request.ComposerId, StringComparison.Ordinal) &&
                string.Equals(draft.SendAttemptRevision, request.Revision, StringComparison.Ordinal) &&
                string.Equals(draft.AccountKey, owner.LocalAccountKey, StringComparison.Ordinal);
            var scope = new PluginComposeTakeover(runtime.Binding, owner, request, _contributors, IsCurrent, ct);
            if (!takeovers.TryOpen(scope)) return TakeoverFailed();
            PluginValue? result = null;
            try
            {
                var args = PluginComposeSendPayload.Encode(request.Payload with { LabelToken = scope.Token });
                var answer = await fenced.InvokeHookWhileCurrentAsync(hook, args, HookPolicies.For(hook).Budget,
                    write => sessions.TryRunWhileCurrent(_captures, () => !ct.IsCancellationRequested && IsCurrent() && write()), ct).ConfigureAwait(false);
                if (answer.IsOk) result = answer.Value;
            }
            catch (PluginValueFormatException) { }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || runtime.Binding.Lifetime.IsCancellationRequested) { }
            finally { takeovers.Close(scope); }
            var (issued, receipt) = scope.Outcome();
            if (receipt is not null) return Result<RawMessageReceipt?>.Ok(receipt);
            if (issued) return Result<RawMessageReceipt?>.Fail(new RawMessageOutcomeUnknown());
            if (result is null) return TakeoverFailed();
            if (result is { Kind: PluginValueKind.Boolean, BooleanValue: false } ||
                (result.Kind == PluginValueKind.Object && result.PropertyNames.Contains("abort", StringComparer.Ordinal) &&
                    result.Property("abort") is { Kind: PluginValueKind.Boolean, BooleanValue: true }))
                return Result<RawMessageReceipt?>.Fail(new ValidationError("An add-on stopped this send."));
            return Result<RawMessageReceipt?>.Ok(null);
        }

        private static Result<RawMessageReceipt?> TakeoverFailed() =>
            Result<RawMessageReceipt?>.Fail(new ValidationError("An add-on could not complete the send."));

        public void NotifySent(EmailSentPayload payload) => _ = DeliverSentAsync(payload);

        private async Task DeliverSentAsync(EmailSentPayload payload)
        {
            const string hook = HookNames.OnAfterEmailSend;
            if (!string.Equals(owner.LocalAccountKey, payload.AccountId, StringComparison.Ordinal) || !sessions.IsCurrent(owner)) return;
            PluginValue? args = null;
            foreach (var runtime in Owners(hook, "email:read"))
            {
                if (runtime is not IPluginFencedHookRuntime fenced) continue;
                args ??= PluginOutgoingPayload.TryEncodeSent(payload);
                if (args is null) return;
                try
                {
                    _ = await fenced.InvokeHookWhileCurrentAsync(hook, args, HookPolicies.For(hook).Budget,
                        write => sessions.TryRunWhileCurrent(_captures, () =>
                            HasGrant(runtime, "email:read") && write()), runtime.Binding.Lifetime).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (runtime.Binding.Lifetime.IsCancellationRequested) { }
#pragma warning disable CA1031 // A failed observer must leave the send and other observers alone.
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
#pragma warning restore CA1031
            }
        }

        private bool Owns(OutgoingEmailPayload outgoing) =>
            string.Equals(owner.LocalAccountKey, outgoing.AccountId, StringComparison.Ordinal) && sessions.IsCurrent(owner);

        private IPluginRuntimeInstance[] Owners(string hook, string permission = "email:send") =>
            runtimes.HookOwners(hook).Where(runtime => HasGrant(runtime, permission))
                .OrderBy(runtime => runtime.Binding.Package.Id, StringComparer.Ordinal).ToArray();

        private bool HasGrant(IPluginRuntimeInstance runtime, string permission) =>
            ReferenceEquals(runtimes.ActiveRuntime(runtime.Binding, owner.LocalAccountKey, permission), runtime);

        private bool RunWhileCurrent(IPluginRuntimeInstance runtime, Func<bool> write, CancellationToken ct) =>
            sessions.TryRunWhileCurrent(_captures, () =>
                !ct.IsCancellationRequested && HasGrant(runtime, "email:send") && DraftIsCurrent() && write());

        private bool DraftIsCurrent() =>
            _draft is { } outgoing && (outgoing.ComposerId is null || outgoing.IsQuickReply ||
                (currentComposer() is { IsSending: true } draft &&
                    string.Equals(draft.Id, outgoing.ComposerId, StringComparison.Ordinal) &&
                    (_sendRevision is null || string.Equals(draft.SendAttemptRevision, _sendRevision, StringComparison.Ordinal)) &&
                    string.Equals(draft.AccountKey, outgoing.AccountId, StringComparison.Ordinal)));
    }
}
#endif
