// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:209-255 doVacationSet (vacation:write, assistant/c.ts:33,94) behind c.ts:52-68 doVacationSetGuarded: SENDS MAIL. Turning the out-of-office on makes the server answer incoming
// mail on the user's behalf, so it is gated exactly as Bulwark gates it: vacation:write, and no add-on may switch the reply on while the out-of-office forwards mail to another address (set in Settings > Out of office) or while
// that cannot be read (switching it on would start sending the user's mail elsewhere): PermissionDenied, nothing written. Switching it off, or changing text and dates while it stays off, is not refused.
// Changes go through IVacationService.SaveAsync, the service Settings > Out of office saves through (VacationRules, the onBeforeVacationUpdate intercept and onVacationUpdate hooks, the states it was loaded at, so a reply that
// changed in between is a Conflict), merged over the reply as it is now so the audience, forward and calendar block the add-on cannot see are kept. Resolves the reply as saved (PluginVacationView, without forwardActive), or
// null when another add-on stopped the change. An HTML body is cleaned like a body in mail (MailSanitizer). A server without VacationResponse is Unavailable. Deviation: 10 changes a minute for each add-on (Bulwark has none).
// Stalwart crates/jmap/src/vacation/set.rs.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginVacationSet(IVacationService vacation, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("vacation.set", "vacation:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "vacation.write";

    protected override int RateLimit => 10;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginVacationInput.TryRead(PluginMailArgs.At(arguments, 0)) is null ? PluginErrorCode.InvalidArguments : null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var input = PluginVacationInput.TryRead(PluginMailArgs.At(arguments, 0))!;
        var session = capture!.Session;
        if (!session.Client.Session.Has(VacationService.Capability)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var (html, derivedText) = await Task.Run(input.CleanHtml, ct).ConfigureAwait(false);
        var loaded = await vacation.LoadSnapshotAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var current = loaded.Value!;
        if (input.IsEnabled && PluginVacationView.ForwardActive(session, current) != false) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        var next = current with
        {
            Enabled = input.IsEnabled,
            Subject = input.Subject ?? current.Subject,
            TextBody = input.TextBody ?? (input.HasHtml && html is not null ? derivedText ?? "" : current.TextBody),
            HtmlBody = input.HasHtml ? html : current.HtmlBody,
            From = input.HasFrom ? input.From : current.From,
            To = input.HasTo ? input.To : current.To,
        };
        if (VacationRules.Problem(next) is not null) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await vacation.SaveAsync(capture.LocalAccountKey, next, ct).ConfigureAwait(false);
        var b = new PluginValueBuilder();
        if (!saved.IsOk)
        {
            if (ReferenceEquals(saved.Error, FilterAccess.StoppedByPlugin)) return Success(b, b.Null());
            return saved.Error!.Message.Contains("may have been saved", StringComparison.Ordinal) ? PluginCallOutcome.Failed(PluginErrorCode.UnknownOutcome) : PluginRulesAccess.Fail(saved.Error);
        }

        return Success(b, PluginVacationView.Project(b, saved.Value!, null, false));
    }
}
