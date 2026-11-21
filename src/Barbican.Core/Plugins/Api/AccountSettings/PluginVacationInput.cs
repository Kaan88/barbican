// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:209-255 doVacationSet input (readObject with isEnabled, subject, textBody, htmlBody, fromDate, toDate; isEnabled required; subject at most 500, bodies at most 64,000; an HTML body that
// has no text is null, otherwise cleaned and, without a text body, its text becomes the text body; fromDate and toDate a date and time with a zone such as 2026-05-01T00:00:00Z, or null; toDate after fromDate) over lib/sieve/period.ts:15-25
// isPeriodBoundary. Deviation: Barbican keeps the reply's period as whole UTC days, so a fromDate must be midnight UTC (T00:00:00) and a toDate the last second of a UTC day (T23:59:59); any other moment is refused
// instead of being rounded to a different period than the add-on asked for.
using System.Globalization;
using System.Text.RegularExpressions;
using Barbican.Core.Mail.Body;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed partial class PluginVacationInput
{
    internal const int MaxSubject = 500;
    internal const int MaxBody = 64_000;
    private static readonly string[] Keys = ["isEnabled", "subject", "textBody", "htmlBody", "fromDate", "toDate"];

    private PluginVacationInput()
    {
    }

    internal bool IsEnabled { get; private set; }

    internal string? Subject { get; private set; }

    internal string? TextBody { get; private set; }

    /// <summary>The raw HTML the add-on gave; null when absent or null (cleaned at <see cref="CleanHtml"/>).</summary>
    internal string? Html { get; private set; }

    internal bool HasHtml { get; private set; }

    internal bool HasFrom { get; private set; }

    internal DateOnly? From { get; private set; }

    internal bool HasTo { get; private set; }

    internal DateOnly? To { get; private set; }

    internal static PluginVacationInput? TryRead(PluginValue? input)
    {
        if (!PluginMailArgs.TryOptions(input, Keys, true, out var data) || data is null) return null;
        if (PluginMailArgs.Prop(data, "isEnabled") is not { Kind: PluginValueKind.Boolean } enabled) return null;
        var result = new PluginVacationInput { IsEnabled = enabled.BooleanValue };
        if (PluginMailArgs.Prop(data, "subject") is { } subject)
        {
            if (!PluginMailArgs.TryString(subject, MaxSubject, out var text)) return null;
            result.Subject = text;
        }

        if (PluginMailArgs.Prop(data, "textBody") is { } body)
        {
            if (!PluginMailArgs.TryString(body, MaxBody, out var text)) return null;
            result.TextBody = text;
        }

        if (PluginMailArgs.Prop(data, "htmlBody") is { } html)
        {
            result.HasHtml = true;
            if (html.Kind != PluginValueKind.Null)
            {
                if (!PluginMailArgs.TryString(html, MaxBody, out var text)) return null;
                result.Html = text;
            }
        }

        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        if (PluginMailArgs.Prop(data, "fromDate") is { } fromValue)
        {
            result.HasFrom = true;
            if (!TryMoment(fromValue, out from)) return null;
            if (from is { } f) result.From = DateOnly.FromDateTime(f.UtcDateTime);
            if (from is { } f2 && f2.UtcDateTime.TimeOfDay != TimeSpan.Zero) return null;
        }

        if (PluginMailArgs.Prop(data, "toDate") is { } toValue)
        {
            result.HasTo = true;
            if (!TryMoment(toValue, out to)) return null;
            if (to is { } t) result.To = DateOnly.FromDateTime(t.UtcDateTime);
            if (to is { } t2 && t2.UtcDateTime.TimeOfDay != new TimeSpan(23, 59, 59)) return null;
        }

        return from is { } start && to is { } end && end <= start ? null : result;
    }

    /// <summary>The cleaned HTML and its text: null HTML when there is no text in it (host-api-settings.ts:228-233). Parses the markup: run it off the UI thread.</summary>
    internal (string? Html, string? Text) CleanHtml()
    {
        if (Html is not { Length: > 0 } html || MailHtml.HtmlToText(html).Trim().Length == 0) return (null, null);
        var clean = MailSanitizer.Clean(html, out _);
        return (clean, MailHtml.HtmlToText(clean));
    }

    // null is allowed (it clears the date); anything else is a moment with its zone spelled out
    private static bool TryMoment(PluginValue value, out DateTimeOffset? moment)
    {
        moment = null;
        if (value.Kind == PluginValueKind.Null) return true;
        if (value.Kind != PluginValueKind.String || !IsoWithZone().IsMatch(value.StringValue)) return false;
        if (!DateTimeOffset.TryParse(value.StringValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.UtcDateTime.Year is < 1 or > 9999) return false;
        moment = parsed;
        return true;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,3})?)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex IsoWithZone();
}
