// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:356,2460 and host-api-notify.ts:1-120 (notifications.show, ui:notifications): { title (1-120, one line), body (at most 300, one line), tag (1-64 of letters, digits and . _ : -), action: { label (1-40),
// url (absolute http or https, at most 2048, no user name or password) } }; at most 5 a minute for each add-on (RateLimited); resolves 'toast' (the app showed it) or 'system'. Every notification names the add-on that sent it: the host's toast presenter
// adds its attributed source (PluginToastMethods.SourceLabel). Text is cut to one line like host-api-notify.ts:25-31 oneLine (control characters become spaces, direction marks go).
// An action's URL may only point at a server the add-on already talks to (its approved HTTP origins; a wildcard origin matches exactly one label), otherwise any add-on with ui:notifications could put an app-styled link to any site in
// front of the user: a URL outside them is refused (InvalidArguments).
// Deviations: Barbican has no system-notification seam, so the result is always 'toast'; the toast has no link button, so a valid action is checked and then not drawn; there is no notification-centre list and no onNotificationClick
// round trip; the toast carries "title: body" as one message (the toast's own source label names the add-on). The presenter is injected so Core stays free of UI types.
using System.Text.RegularExpressions;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed partial class PluginNotificationsShow(IPluginMailRateLimiter limiter, Func<string, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present)
    : PluginScopedMethod("notifications.show", "ui:notifications", mayMutate: true, limiter: limiter)
{
    internal const int PerMinute = 5;
    internal const int MaxTitle = 120;
    internal const int MaxBody = 300;
    internal const int MaxLabel = 40;
    private static readonly string[] Keys = ["title", "body", "tag", "action"];

    protected override string? RateKind => "notifications";

    protected override int RateLimit => PerMinute;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryRead(PluginMailArgs.At(arguments, 0)) is null ? PluginErrorCode.InvalidArguments : null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var (title, body, actionUrl) = TryRead(PluginMailArgs.At(arguments, 0))!.Value;
        if (actionUrl is not null && !OriginAllowed(new Uri(actionUrl), context.Grant.ApprovedHttpOrigins)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var failure = await present(body.Length == 0 ? title : title + ": " + body, context, ct).ConfigureAwait(false);
        if (failure is { } code) return PluginCallOutcome.Failed(code);
        var b = new PluginValueBuilder();
        return Success(b, b.String("toast"));
    }

    // the text on one line: control characters become spaces, direction marks go (host-api-notify.ts:25-31)
    internal static string OneLine(string text) => DirectionMarks().Replace(ControlRuns().Replace(text, " "), "").Trim();

    private static (string Title, string Body, string? ActionUrl)? TryRead(PluginValue? input)
    {
        if (!PluginMailArgs.TryOptions(input, Keys, true, out var data) || data is null) return null;
        if (PluginMailArgs.Prop(data, "title") is not { Kind: PluginValueKind.String } t || t.StringValue.Length is 0 or > MaxTitle || OneLine(t.StringValue).Length == 0) return null;
        var body = "";
        if (PluginMailArgs.Prop(data, "body") is { } bodyValue)
        {
            if (!PluginMailArgs.TryString(bodyValue, MaxBody, out var text)) return null;
            body = OneLine(text);
        }

        if (PluginMailArgs.Prop(data, "tag") is { } tag && (tag.Kind != PluginValueKind.String || !TagShape().IsMatch(tag.StringValue))) return null;
        string? url = null;
        if (PluginMailArgs.Prop(data, "action") is { } action && !TryAction(action, out url)) return null;
        return (OneLine(t.StringValue), body, url);
    }

    private static bool TryAction(PluginValue action, out string? url)
    {
        url = null;
        if (!PluginMailArgs.TryOptions(action, ["label", "url"], true, out var a) || a is null) return false;
        if (PluginMailArgs.Prop(a, "label") is not { Kind: PluginValueKind.String } label || label.StringValue.Length is 0 or > MaxLabel || OneLine(label.StringValue).Length == 0) return false;
        if (PluginMailArgs.Prop(a, "url") is not { Kind: PluginValueKind.String } raw || raw.StringValue.Length is 0 or > 2048) return false;
        if (!Uri.TryCreate(raw.StringValue, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || parsed.UserInfo.Length > 0) return false;
        url = parsed.AbsoluteUri;
        return true;
    }

    // the action's server must be one the add-on already talks to (an exact origin, or a wildcard "https://*.host" with exactly one label before the host)
    private static bool OriginAllowed(Uri uri, IEnumerable<string> approved)
    {
        foreach (var entry in approved)
        {
            var wildcard = entry.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase);
            if (!Uri.TryCreate(wildcard ? entry.Remove(8, 2) : entry, UriKind.Absolute, out var origin) || origin.Port != uri.Port
                || !string.Equals(origin.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase)) continue;
            if (!wildcard && string.Equals(origin.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase)) return true;
            if (wildcard && uri.IdnHost.EndsWith("." + origin.IdnHost, StringComparison.OrdinalIgnoreCase)
                && !uri.IdnHost[..(uri.IdnHost.Length - origin.IdnHost.Length - 1)].Contains('.', StringComparison.Ordinal)
                && uri.IdnHost.Length > origin.IdnHost.Length + 1) return true;
        }

        return false;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,64}$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex TagShape();

    [GeneratedRegex(@"[\u0000-\u001f\u007f]+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex ControlRuns();

    [GeneratedRegex(@"[؜‎‏‪-‮⁦-⁩]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex DirectionMarks();
}
