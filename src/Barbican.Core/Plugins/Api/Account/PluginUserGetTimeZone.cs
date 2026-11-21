// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2011-2015 userTimeZone (no permission, host-api.ts:200): the IANA zone the app shows times in, the
// user's `timeZone` setting unless it is "auto", else the machine's zone. A stored value that is not an IANA-shaped name is ignored. Windows zone ids
// are converted to IANA ids; a zone with no IANA id reads as "UTC".
using System.Text.RegularExpressions;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed partial class PluginUserGetTimeZone(IPrefsStore prefs, TimeProvider time) : PluginScopedMethod("user.getTimeZone", null)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var setting = prefs.Current.Str("timeZone", "auto").Trim();
        var zone = setting.Length > 0 && !string.Equals(setting, "auto", StringComparison.Ordinal) && IanaShape().IsMatch(setting) ? setting : Local(time.LocalTimeZone);
        var b = new PluginValueBuilder();
        return Task.FromResult(Success(b, b.String(zone)));
    }

    private static string Local(TimeZoneInfo zone)
    {
        if (zone.Id.Contains('/', StringComparison.Ordinal)) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) && !string.IsNullOrEmpty(iana) ? iana : "UTC";
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+){0,2}$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex IanaShape();
}
