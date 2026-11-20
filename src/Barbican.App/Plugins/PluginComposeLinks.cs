#if WINDOWS
// Mirrors Bulwark lib/plugin-composer-hooks.ts:62-70 and lib/plugin-link-params.ts.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Mailto;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Compose;

namespace Barbican.App.Plugins;

internal sealed class PluginComposeLinks(
    IComposerService composer, PluginComposerAccess access, PluginBackgroundRuntimes runtimes, IPluginSessionSource sessions) : IComposeLinkAddOns
{
    private const string Hook = HookNames.OnComposeFromLink;
    private static readonly PluginValueLimits PayloadLimits = new(MaxStringBytes: 400_000);

    public async Task DeliverAsync(ComposerViewModel draft, IReadOnlyDictionary<string, string> parameters, CancellationToken ct)
    {
        if (!IsOpen(draft)) return;
        var captured = sessions.CaptureOwner(draft.AccountKey);
        if (!captured.IsOk || captured.Value is not { } owner ||
            !string.Equals(owner.LocalAccountKey, draft.AccountKey, StringComparison.Ordinal)) return;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void Changed(object? sender, EventArgs args)
        {
            if (!ReferenceEquals(composer.Current, draft)) lifetime.Cancel();
        }

        composer.OpenChanged += Changed;
        try
        {
            if (!IsOpen(draft)) return;
            await runtimes.WaitUntilInitializedAsync(lifetime.Token).ConfigureAwait(true);
            var owners = runtimes.HookOwners(Hook).ToDictionary(runtime => runtime.Binding.Package.Id, StringComparer.Ordinal);
            var groups = ComposeLinkParameters.Partition(parameters, owners.Keys);
            IReadOnlyList<PluginSessionCapture> captures = [owner];
            foreach (var (id, group) in groups)
            {
                if (!IsOpen(draft) || !sessions.IsCurrent(owner)) return;
                var runtime = owners[id];
                if (runtime is not IPluginFencedHookRuntime fenced || !MayDeliver(runtime, draft.AccountKey)) continue;
                var payload = Encode(new ComposeLinkContextPayload(draft.Id, group));
                _ = await fenced.InvokeHookWhileCurrentAsync(Hook, payload, HookPolicies.For(Hook).Budget,
                    write => sessions.TryRunWhileCurrent(captures, () =>
                        !lifetime.IsCancellationRequested && IsOpen(draft) && MayDeliver(runtime, draft.AccountKey) &&
                        access.Expose(runtime.Binding, draft.Id, owner) && write()), lifetime.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested && !ct.IsCancellationRequested) { }
        finally { composer.OpenChanged -= Changed; }
    }

    private bool IsOpen(ComposerViewModel draft) =>
        ReferenceEquals(composer.Current, draft) && !draft.IsClosed && !draft.IsSending;

    private bool MayDeliver(IPluginRuntimeInstance runtime, string account) =>
        ReferenceEquals(runtimes.ActiveRuntime(runtime.Binding, account, "compose:read"), runtime) &&
        ReferenceEquals(runtimes.ActiveRuntime(runtime.Binding, account, "compose:write"), runtime);

    private static PluginValue Encode(ComposeLinkContextPayload payload)
    {
        var values = new PluginValueBuilder();
        var parameters = values.Object([.. payload.Params.Select(entry => (entry.Key, values.String(entry.Value)))]);
        return values.Build(values.Array(values.Object(("composerId", values.String(payload.ComposerId)), ("params", parameters))), PayloadLimits);
    }
}
#endif
