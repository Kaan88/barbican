#if WINDOWS
// Mirrors Bulwark lib/plugin-sandbox/host-api.ts:883-961 and lib/outgoing/classification-raw.ts.
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Common;

namespace Barbican.App.Plugins;

internal sealed class PluginRawSendHost(RawMessageSender sender, OrganizationPolicyService policy, PluginPackageHost packages,
    PluginComposeTakeovers takeovers, IDispatcher dispatcher)
    : IPluginRawSendHost
{
    private const string Permission = "email:raw-send";

    public async Task<PluginRawSendOutcome> SendAsync(IPluginInvocationContext context, PluginRawSendRequest request, CancellationToken ct)
    {
        if (context.Binding.Tier == PluginExecutionTier.Untrusted) return new(null, PluginErrorCode.PermissionDenied);
        if (!takeovers.TryAuthorize(context.Binding, request, out var takeover)) return new(null, PluginErrorCode.PermissionDenied);
        var capture = takeover is null ? context.CaptureActive() : context.CaptureOwner(takeover.Owner.LocalAccountKey);
        if (capture.Value is not { } owner || (takeover is not null && !ReferenceEquals(owner.Session, takeover.Owner.Session)))
            return new(null, PluginErrorCode.NoSession);
        if (!context.Demand(Permission, owner.LocalAccountKey).IsOk) return new(null, PluginErrorCode.PermissionDenied);
        if (takeover is not null && !context.Demand("email:send", owner.LocalAccountKey).IsOk)
            return new(null, PluginErrorCode.PermissionDenied);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return new(null, PluginErrorCode.StaleContext);

        var raw = new RawMessageRequest(owner.LocalAccountKey, request.IdentityId, request.Bytes.BinaryBytes(), request.EnvelopeRecipients)
        {
            ScheduledAt = request.ScheduledAt,
            SubmitOnly = request.SubmitOnly,
        };
        var stale = false;
        async Task<bool> AuthorizeAsync(bool submission, Func<Func<bool>, bool> guard, Func<bool> issue, CancellationToken token)
        {
            var issued = false;
            if (takeover is null)
                issued = await TryIssueAsync(context, owner.LocalAccountKey, submission, guard, issue, null, token).ConfigureAwait(false);
            else
                await dispatcher.InvokeAsync(async () => issued = await TryIssueAsync(context, owner.LocalAccountKey, submission,
                    guard, issue, takeover, token).ConfigureAwait(true)).ConfigureAwait(false);
            stale |= !issued;
            return issued;
        }

        var snapshot = new AccountSessionSnapshot(owner.Session, owner.ActiveSelectionGeneration);
        var result = await (takeover is null ? sender.SendUnderPolicyAsync(snapshot, raw, policy, AuthorizeAsync, ct)
            : sender.SendTakeoverAsync(snapshot, raw, takeover.Request, AuthorizeAsync, ct)).ConfigureAwait(false);
        if (result.IsOk && result.Value is { } accepted) takeover?.Record(accepted);
        return result.IsOk ? new(result.Value, null) : new(null, result.Error switch
        {
            RawMessageOutcomeUnknown => PluginErrorCode.UnknownOutcome,
            RawMessagePolicyDenied => PluginErrorCode.PermissionDenied,
            _ when stale => PluginErrorCode.StaleContext,
            ValidationError => PluginErrorCode.InvalidArguments,
            _ => PluginErrorCode.Failed,
        });
    }

    private async Task<bool> TryIssueAsync(IPluginInvocationContext context, string accountKey, bool submission,
        Func<Func<bool>, bool> guard, Func<bool> issue, PluginComposeTakeover? takeover, CancellationToken ct)
    {
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(true)) return false;
        var binding = context.Binding;
        bool Issue()
        {
            if (ct.IsCancellationRequested) return false;
            if (submission) context.MarkMutationIssued();
            return issue();
        }
        if (takeover is not null)
            return await packages.TryRunWithGrantsAsync(takeover.Bindings, () =>
                guard(() => PluginRuntimeBinding.TryWriteWhileAllAlive(takeover.Bindings, () => takeover.TryIssue(submission, Issue))),
                ct).ConfigureAwait(true);
        return await packages.TryPostAsync(binding, grant => grant.Authorizes(binding.Package, binding.GrantGeneration, Permission, accountKey) &&
            grant.AccountKeys.SetEquals(context.Grant.AccountKeys) && grant.Permissions.SetEquals(context.Grant.Permissions) &&
            grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) &&
            guard(() => PluginRuntimeBinding.TryWriteWhileAllAlive([binding], Issue)), ct).ConfigureAwait(true);
    }
}
#endif
