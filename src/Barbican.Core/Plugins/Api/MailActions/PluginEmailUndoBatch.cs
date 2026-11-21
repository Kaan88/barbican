// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:148-160 doEmailUndoBatch (email:write, c.ts:31) over c.undo.ts:46-65 withUndoToken (only the add-on that got the token, in the same login, for the same kind, within
// 30 minutes; one undo at a time per token; spent only when the undo went through): the entry leaves the app's undo history first so the user's Undo cannot run it again, then the messages go back to their folders, read state and
// tags. An unknown, spent, expired or foreign token is Unavailable; a undo the server refused is Failed and the token stays for another try. Resolves undefined.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailUndoBatch(PluginMailActionHost host) : PluginMailActionMethod("email.undoBatch", "email:write", host, true, slow: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var claim = Host.Undo.Begin(arguments.Item(0).StringValue, context.Binding.Package.Id, capture!.LocalAccountKey, PluginEmailBatch.UndoKind);
        if (claim is null) return Fail(PluginErrorCode.Unavailable);
        try
        {
            if (claim.Data.Str("restore") is not { } key || Host.Restore(key) is not { } restore) return Fail(PluginErrorCode.Unavailable);
            if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
            // off the history first, so the user's Undo cannot run it again; an entry already taken by that Undo has run (or is running) the same action
            if (restore.EntryId is { } entry) _ = Host.History?.Remove(entry);
            if (!await restore.RunOnceAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.Failed);
            claim.Spend();
            claim = null;
            return Done();
        }
        finally
        {
            claim?.Release();
        }
    }
}
