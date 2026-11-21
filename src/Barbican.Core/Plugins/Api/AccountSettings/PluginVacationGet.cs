// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:197-207 doVacationGet (vacation:read, assistant/c.ts:32,93) wrapped by c.ts:35-50 doVacationGetWithForward: the out-of-office reply as { isEnabled, subject, htmlBody,
// textBody, fromDate, toDate, forwardActive } (PluginVacationView). A server without VacationResponse is Unavailable. Read through IVacationService.LoadSnapshotAsync, the service Settings > Out of office loads through, without
// its load hook and without the calendar reconciliation a normal load runs (a read must not write calendar events). Stalwart crates/jmap/src/vacation/get.rs.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginVacationGet(IVacationService vacation) : PluginScopedMethod("vacation.get", "vacation:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        if (!session.Client.Session.Has(VacationService.Capability)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var loaded = await vacation.LoadSnapshotAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var b = new PluginValueBuilder();
        return Success(b, PluginVacationView.Project(b, loaded.Value!, PluginVacationView.ForwardActive(session, loaded.Value!), true));
    }
}
