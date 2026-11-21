// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:251-302 doEmailMove / doEmailArchive (email:write, host-api.ts:344-345; at most 100 distinct message ids; the folder must be one of the signed-in account's own, not a shared one;
// archive needs the account's Archive folder; 20 moves a minute per add-on, shared by both methods; the messages of the signed-in account only; resolves undefined) and lib/jmap/client.ts batchMoveEmails :4134 (the whole folder set
// is replaced by the target). Stalwart crates/jmap/src/email/set.rs (mailboxIds). The same intercept and observer hooks as the list's own move and archive (onBeforeEmailMove / onAfterEmailMove,
// onBeforeEmailArchive / onEmailArchive; MailActionService.Moves.cs) run; an intercept that says no stops the move and the add-on gets Failed (Bulwark resolves with nothing moved).
// Deviations: the archive method moves into the Archive folder itself, not into the per-year or per-month folders of the user's archive mode; no list or reader is changed directly (the sync brings the change in).
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailMove(PluginMailActionHost host, bool archive) : PluginMailActionMethod(archive ? "email.archive" : "email.move", "email:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments)
    {
        if (!PluginMailActionArgs.TryIdList(PluginMailArgs.At(arguments, 0), PluginMailActionArgs.MaxMoveIds, out _)) return PluginErrorCode.InvalidArguments;
        return archive || PluginMailArgs.IsId(PluginMailArgs.At(arguments, 1)) ? null : PluginErrorCode.InvalidArguments;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        _ = PluginMailActionArgs.TryIdList(PluginMailArgs.At(arguments, 0), PluginMailActionArgs.MaxMoveIds, out var ids);
        var folders = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!folders.IsOk) return Fail(PluginErrorCode.Failed);
        var target = archive ? folders.Value!.All.FirstOrDefault(m => m.Role == MailboxRole.Archive) : folders.Value!.All.FirstOrDefault(m => string.Equals(m.Id, arguments.Item(1).StringValue, StringComparison.Ordinal));
        if (target is null) return Fail(PluginErrorCode.Unavailable);
        if (!Within(context, "email.move", MovesPerMinute)) return Fail(PluginErrorCode.RateLimited);
        var hooks = Host.Hooks;
        if (hooks is not null && !await AllowedAsync(hooks, ids, target.Id, ct).ConfigureAwait(false))
        {
            return Fail(PluginErrorCode.Failed);
        }

        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var moved = await Host.Writers.ForSession(session).MoveAsync(ids, null, target.Id, ct).ConfigureAwait(false);
        if (!Accepted(moved)) return Fail(PluginErrorCode.Failed);
        if (hooks is not null) await TellAsync(hooks, ids, target.Id, ct).ConfigureAwait(false);
        return Done();
    }

    private async Task<bool> AllowedAsync(IHookBus hooks, IReadOnlyList<string> ids, string to, CancellationToken ct)
    {
        var hook = archive ? HookNames.OnBeforeEmailArchive : HookNames.OnBeforeEmailMove;
        if (!hooks.HasHandlers(hook)) return true;
        return archive
            ? await hooks.InterceptAsync(hook, new EmailIdsPayload(ids), ct).ConfigureAwait(false)
            : await hooks.InterceptAsync(hook, new EmailMovePayload(ids, null, to), ct).ConfigureAwait(false);
    }

    private async Task TellAsync(IHookBus hooks, IReadOnlyList<string> ids, string to, CancellationToken ct)
    {
        var hook = archive ? HookNames.OnEmailArchive : HookNames.OnAfterEmailMove;
        if (!hooks.HasHandlers(hook)) return;
        if (archive) await hooks.NotifyAsync(hook, new EmailIdsPayload(ids), ct).ConfigureAwait(false);
        else await hooks.NotifyAsync(hook, new EmailMovePayload(ids, null, to), ct).ConfigureAwait(false);
    }
}
