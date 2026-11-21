// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:99-125 doEmailList (options mailboxId / limit 1-100 default 50 / position 0-1,000,000;
// MAX_PLUGIN_EMAIL_PAGE = 100; unknown options refused) and host-api.ts:335,2444.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginEmailList(IPluginMailReaders readers) : PluginMailMethod("email.list", readers)
{
    internal const int MaxPage = 100;
    private static readonly string[] Allowed = ["mailboxId", "limit", "position"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        _ = TryParse(arguments, out var mailbox, out var limit, out var position);
        return PluginMailListing.RunAsync(Readers, context, session, mailbox, null, null, limit, position, ct);
    }

    private static bool TryParse(PluginValue arguments, out string? mailbox, out int limit, out int position)
    {
        mailbox = null;
        limit = 50;
        position = 0;
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, false, out var options)) return false;
        var id = PluginMailArgs.Prop(options, "mailboxId");
        if (id is not null)
        {
            if (!PluginMailArgs.IsId(id)) return false;
            mailbox = id.StringValue;
        }
        return PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "limit"), 1, MaxPage, 50, out limit)
            && PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "position"), 0, 1_000_000, 0, out position);
    }
}
