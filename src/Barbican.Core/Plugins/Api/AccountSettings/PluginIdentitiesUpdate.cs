// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:329,2438 and host-api-settings.ts:125-185 doIdentitiesUpdate (identity:write): changes one sender identity's name, reply-to addresses or signatures; resolves the identity as
// { id, name, email, replyTo, bcc, htmlSignature, textSignature } (plugin-projection.ts:212-222: replyTo and bcc as the addresses joined by ", " or null), or null when another add-on stopped the change. CHANGES THE SENDER IDENTITY, so
// it is gated exactly as Bulwark gates it: identity:write, the identity must exist, the address can never change (a patch naming email is refused, the other fields only: PluginIdentityPatch), and the change goes through
// IAccountIdentities.UpdateAsync, the service Settings > Identities saves through (AccountIdentityRules, the onBeforeIdentityChange intercept and onIdentityUpdate hooks, the same Identity/set, Stalwart crates/jmap/src/identity/set.rs).
// The result names fields one by one: a server property Barbican does not model never reaches the add-on. Deviation: 20 changes a minute for each add-on (Bulwark has none).
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Account;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginIdentitiesUpdate(IAccountIdentities identities, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("identities.update", "identity:write", mayMutate: true, limiter: limiter)
{
    // the failure text of an intercept hook in AccountIdentities
    private const string StoppedByPlugin = "A plug-in stopped this change.";

    protected override string? RateKind => "identities.write";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginIdentityPatch.TryRead(PluginMailArgs.At(arguments, 1)) is not null ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        var patch = PluginIdentityPatch.TryRead(PluginMailArgs.At(arguments, 1))!;
        var listed = await identities.ListAsync(capture!.LocalAccountKey, ct).ConfigureAwait(false);
        if (!listed.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!listed.Value!.Any(i => string.Equals(i.Id, id, StringComparison.Ordinal))) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var html = await Task.Run(patch.CleanedHtml, ct).ConfigureAwait(false);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await identities.UpdateAsync(capture.LocalAccountKey, id, new AccountIdentityEdit(patch.Name, null, patch.ReplyTo, null, patch.TextSignature, html), ct).ConfigureAwait(false);
        var b = new PluginValueBuilder();
        if (!saved.IsOk)
        {
            return string.Equals(saved.Error!.Message, StoppedByPlugin, StringComparison.Ordinal) ? Success(b, b.Null()) : PluginRulesAccess.Fail(saved.Error);
        }

        return Success(b, Project(b, saved.Value!));
    }

    private static int Project(PluginValueBuilder b, AccountIdentity i) => b.Object(
        ("id", b.String(i.Id)), ("name", b.String(i.Name)), ("email", b.String(i.Email)), ("replyTo", Addresses(b, i.ReplyTo)), ("bcc", Addresses(b, i.Bcc)),
        ("htmlSignature", b.String(i.HtmlSignature)), ("textSignature", b.String(i.TextSignature)));

    private static int Addresses(PluginValueBuilder b, string? list)
    {
        var emails = AccountIdentityRules.ParseList(list).Select(a => a.Email).ToList();
        return emails.Count == 0 ? b.Null() : b.String(string.Join(", ", emails));
    }
}
