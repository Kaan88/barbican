// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:197,2222 and host-api-account.ts:250-270 doAccountDataSet (email:write; 20 changes a minute): replaces, or with null removes, the add-on's own value in the account's shared Inbox metadata; value is required
// (null removes it), must be JSON and, unless null, at most 4096 bytes (half the 8 KB Stalwart takes per entry); resolves undefined. A server that keeps no metadata, or an id that cannot be a metadata key, is Unavailable. Written through
// FolderAdminMetadata.UpdateAsync, the one writer of Inbox metadata (read, patch "metadata/org.bulwarkmail.plugin.<id>" with ifInState, retry on stateMismatch): no other entry is touched. See PluginAccountData for the key and the account.
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginAccountDataSet(IPluginMailRateLimiter limiter) : PluginScopedMethod("accountData.set", "email:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "account-data";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginAccountData.TryAccountArgument(PluginMailArgs.At(arguments, 0), out _) && TryValue(PluginMailArgs.At(arguments, 1), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginAccountData.TryAccountArgument(PluginMailArgs.At(arguments, 0), out var accountId);
        _ = TryValue(PluginMailArgs.At(arguments, 1), out var value);
        var client = capture!.Session.Client;
        if (!PluginAccountData.IsOwnAccount(client, accountId)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (PluginAccountData.KeyOf(context.Binding.Package.Id) is not { } key || !PluginAccountData.Supported(client)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        // a fresh patch for every try: a node has one parent
        var written = await FolderAdminMetadata.UpdateAsync(client, _ => new JsonObject { ["metadata/" + PluginAccountData.Namespace + key] = value?.DeepClone() }, ct).ConfigureAwait(false);
        if (!written.IsOk) return PluginRulesAccess.Fail(written.Error);
        var b = new PluginValueBuilder();
        return Success(b, b.Undefined());
    }

    // value is required (null removes the entry), must be JSON and at most 4096 bytes unless null
    private static bool TryValue(PluginValue? value, out JsonNode? json)
    {
        json = null;
        if (value is null) return false;
        try
        {
            var element = value.ToJson();
            if (element.ValueKind == System.Text.Json.JsonValueKind.Null) return true;
            if (Encoding.UTF8.GetByteCount(element.GetRawText()) > PluginAccountData.MaxBytes) return false;
            json = JsonNode.Parse(element.GetRawText());
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
