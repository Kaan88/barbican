#if WINDOWS
// Mirrors Bulwark lib/plugin-sandbox/host-api.ts:883-961 and lib/outgoing/classification-raw.ts.
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins;

internal sealed class PluginRawSendHost(RawMessageSender sender, OrganizationPolicyService policy, PluginPackageHost packages)
    : IPluginRawSendHost
{
    private const string Permission = "email:raw-send";

    public async Task<PluginRawSendOutcome> SendAsync(IPluginInvocationContext context, PluginRawSendRequest request, CancellationToken ct)
    {
        if (request.LabelToken is not null) return new(null, PluginErrorCode.PermissionDenied);
        if (context.CaptureActive().Value is not { } owner) return new(null, PluginErrorCode.NoSession);
        if (!context.Demand(Permission, owner.LocalAccountKey).IsOk) return new(null, PluginErrorCode.PermissionDenied);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return new(null, PluginErrorCode.StaleContext);

        var raw = new RawMessageRequest(owner.LocalAccountKey, request.IdentityId, request.Bytes.BinaryBytes(), request.EnvelopeRecipients)
        {
            ScheduledAt = request.ScheduledAt,
            SubmitOnly = request.SubmitOnly,
        };
        var stale = false;
        async Task<bool> AuthorizeAsync(bool submission, Func<Func<bool>, bool> guard, Func<bool> issue, CancellationToken token)
        {
            var issued = await TryIssueAsync(context, owner.LocalAccountKey, submission, guard, issue, token).ConfigureAwait(false);
            stale |= !issued;
            return issued;
        }

        var result = await sender.SendUnderPolicyAsync(new AccountSessionSnapshot(owner.Session, owner.ActiveSelectionGeneration),
            raw, policy, AuthorizeAsync, ct).ConfigureAwait(false);
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
        Func<Func<bool>, bool> guard, Func<bool> issue, CancellationToken ct)
    {
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return false;
        var binding = context.Binding;
        return await packages.TryPostAsync(binding, grant => grant.Authorizes(binding.Package, binding.GrantGeneration, Permission, accountKey) &&
            grant.AccountKeys.SetEquals(context.Grant.AccountKeys) && grant.Permissions.SetEquals(context.Grant.Permissions) &&
            grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) &&
            guard(() => PluginRuntimeBinding.TryWriteWhileAllAlive([binding], () =>
            {
                if (ct.IsCancellationRequested) return false;
                if (submission) context.MarkMutationIssued();
                return issue();
            })), ct).ConfigureAwait(false);
    }
}
#endif
