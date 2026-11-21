// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:196,2221 and host-api-account.ts:226-248 doAccountDataGet (email:read; 30 reads a minute): resolves { supported, value }: the add-on's own value in the account's shared Inbox metadata, null when it has
// none; supported false (and null) when the server keeps no metadata or the add-on's id cannot be a metadata key. See PluginAccountData for the key and the account. An account other than the login's own is Unavailable.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginAccountDataGet(IPluginMailRateLimiter limiter) : PluginScopedMethod("accountData.get", "email:read", limiter: limiter)
{
    protected override string? RateKind => "account-data-read";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginAccountData.TryAccountArgument(PluginMailArgs.At(arguments, 0), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginAccountData.TryAccountArgument(PluginMailArgs.At(arguments, 0), out var accountId);
        var client = capture!.Session.Client;
        if (!PluginAccountData.IsOwnAccount(client, accountId)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var b = new PluginValueBuilder();
        var key = PluginAccountData.KeyOf(context.Binding.Package.Id);
        if (key is null || !PluginAccountData.Supported(client)) return Success(b, Answer(b, false, null));
        var read = await FolderAdminMetadata.ReadAsync(client, ct).ConfigureAwait(false);
        if (!read.IsOk) return Success(b, Answer(b, false, null));
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var stored = read.Value.Metadata[PluginAccountData.Namespace + key];
        try
        {
            return Success(b, Answer(b, true, stored is null ? null : b.Import(PluginAccountData.ToPluginValue(stored))));
        }
        catch (PluginValueFormatException)
        {
            // an entry the server holds that is too large or too deep for an add-on to receive
            return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        }
    }

    private static int Answer(PluginValueBuilder b, bool supported, int? value) =>
        b.Object(("supported", b.Boolean(supported)), ("value", value ?? b.Null()));
}
