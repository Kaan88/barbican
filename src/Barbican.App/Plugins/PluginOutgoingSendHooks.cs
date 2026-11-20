#if WINDOWS
// Mirrors Bulwark lib/outgoing/plugin-send-check.ts and lib/plugin-sandbox/hook-payload.ts.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
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
    Func<ComposerViewModel?> currentComposer, IDispatcher dispatcher) : IOutgoingAddOnHooks
{
    public IOutgoingAddOnSend Start(IAccountSession sendingSession) => new Attempt(
        runtimes, packages, sessions, currentComposer, dispatcher, new PluginSessionCapture(sendingSession, null));

    private sealed class Attempt(
        PluginBackgroundRuntimes runtimes, PluginPackageHost packages, IPluginSessionSource sessions,
        Func<ComposerViewModel?> currentComposer, IDispatcher dispatcher, PluginSessionCapture owner) : IOutgoingAddOnSend
    {
        private readonly IReadOnlyList<PluginSessionCapture> _captures = [owner];
        private readonly HashSet<PluginRuntimeBinding> _contributors = [];
        private OutgoingEmailPayload? _draft;

        public async Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct)
        {
            const string hook = HookNames.OnBeforeEmailSend;
            _draft ??= outgoing;
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

        public async Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct)
        {
            var accepted = false;
            bool Issue() => sessions.TryRunWhileCurrent(_captures, () =>
                !ct.IsCancellationRequested && DraftIsCurrent() && PluginRuntimeBinding.TryWriteWhileAllAlive(_contributors, issue));
            await dispatcher.InvokeAsync(async () =>
            {
                accepted = _contributors.Count == 0
                    ? Issue()
                    : await packages.TryRunWithGrantsAsync(_contributors, Issue, ct).ConfigureAwait(true);
            }).ConfigureAwait(false);
            return accepted;
        }

        private bool Owns(OutgoingEmailPayload outgoing) =>
            string.Equals(owner.LocalAccountKey, outgoing.AccountId, StringComparison.Ordinal) && sessions.IsCurrent(owner);

        private IPluginRuntimeInstance[] Owners(string hook) =>
            runtimes.HookOwners(hook).Where(runtime => HasGrant(runtime, "email:send"))
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
                    string.Equals(draft.AccountKey, outgoing.AccountId, StringComparison.Ordinal)));
    }
}
#endif
