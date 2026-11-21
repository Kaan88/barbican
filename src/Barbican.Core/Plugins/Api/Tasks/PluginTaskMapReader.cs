// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:573-766 (TASK_FIELDS, the limits, MEMBER_ID_RE, OFFSET_RE, REL_RE, RELATION_RE, RELATED_UID_RE, readMapChange, readTrigger, alertsOf, readHref, linksOf,
// relatedToOf, keywordsOf): the shapes a plugin may give a task's alerts, links, relations and keywords. JSCalendar: RFC 8984 4.5.2 (alerts, OffsetTrigger, AbsoluteTrigger), 1.4.11 (Link), 1.4.10 (Relation), 4.1.3 (keywords).
// Deviation: Bulwark also takes a path of Bulwark itself ("/mail/...") and stores it as a full URL; Barbican has no web origin, so a link must be an http(s) URL or a mailto: address.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal static partial class PluginTaskMapReader
{
    internal const int MaxAlerts = 5;
    internal const int MaxLinks = 10;
    internal const int MaxRelations = 10;
    internal const int MaxKeywords = 10;
    private const int MaxHref = 2048;
    private const int MaxLinkTitle = 500;
    private const int MaxRelatedUid = 998;
    private const int MaxRelationsPerUid = 4;
    private const int MaxRemovedKey = 1024;
    private const long MaxAlertOffsetSeconds = PluginTimingBuilder.MaxRangeDays * 86_400L;
    private static readonly string[] AlertFields = ["trigger", "action"];
    private static readonly string[] OffsetFields = ["offset", "relativeTo"];
    private static readonly string[] AtFields = ["at"];
    private static readonly string[] LinkFields = ["href", "rel", "title"];
    private static readonly string[] RelationFields = ["relation"];

    internal static bool TryAlerts(PluginValue? value, out PluginTaskMapChange? change) => TryMap(value, MaxAlerts, IsMemberId, ReadAlert, out change);

    internal static bool TryLinks(PluginValue? value, out PluginTaskMapChange? change) => TryMap(value, MaxLinks, IsMemberId, ReadLink, out change);

    internal static bool TryRelations(PluginValue? value, out PluginTaskMapChange? change) => TryMap(value, MaxRelations, IsRelatedUid, ReadRelation, out change);

    internal static bool TryKeywords(PluginValue? value, out PluginTaskMapChange? change) =>
        TryMap(value, MaxKeywords, IsMemberId, entry => entry is { Kind: PluginValueKind.Boolean } && entry.BooleanValue ? JsonValue.Create(true) : null, out change);

    /// <summary>readMapChange (:627-649).</summary>
    private static bool TryMap(PluginValue? value, int max, Func<string, bool> keyOk, Func<PluginValue, JsonNode?> read, out PluginTaskMapChange? change)
    {
        change = null;
        if (value is null) return true;
        if (value.Kind == PluginValueKind.Null)
        {
            change = new PluginTaskMapChange(true, []);
            return true;
        }

        if (value.Kind != PluginValueKind.Object || value.PropertyNames.Count > max) return false;
        var entries = new List<(string, JsonNode?)>();
        foreach (var key in value.PropertyNames)
        {
            var entry = value.Property(key);
            if (entry.Kind == PluginValueKind.Null)
            {
                // a key set must pass keyOk; a key removed may be any key the task could hold, so an add-on can remove entries another app wrote
                if (key.Length is 0 or > MaxRemovedKey || key.Any(char.IsControl)) return false;
                entries.Add((key, null));
                continue;
            }

            if (!keyOk(key) || read(entry) is not { } node) return false;
            entries.Add((key, node));
        }

        change = new PluginTaskMapChange(false, entries);
        return true;
    }

    private static bool IsMemberId(string key) => MemberId().IsMatch(key);

    private static bool IsRelatedUid(string key) => key.Length <= MaxRelatedUid && RelatedUid().IsMatch(key);

    /// <summary>alertsOf / readTrigger (:651-686): an offset from the due (or the start) of at most a year either way, or an instant in UTC.</summary>
    private static JsonNode? ReadAlert(PluginValue entry)
    {
        if (!PluginMailArgs.TryOptions(entry, AlertFields, true, out var a) || a is null) return null;
        var action = "display";
        if (PluginMailArgs.Prop(a, "action") is { } actionValue)
        {
            if (actionValue is not { Kind: PluginValueKind.String } || actionValue.StringValue is not ("display" or "email")) return null;
            action = actionValue.StringValue;
        }

        if (ReadTrigger(PluginMailArgs.Prop(a, "trigger")) is not { } trigger) return null;
        return new JsonObject { ["@type"] = "Alert", ["trigger"] = trigger, ["action"] = action };
    }

    private static JsonObject? ReadTrigger(PluginValue? value)
    {
        if (value is not { Kind: PluginValueKind.Object }) return null;
        if (value.PropertyNames.Contains("at", StringComparer.Ordinal))
        {
            if (!PluginMailArgs.TryOptions(value, AtFields, true, out var at) || !PluginMailArgs.TryString(PluginMailArgs.Prop(at, "at"), 64, out var text) || !PluginWhenParser.TryInstant(text, out var instant)) return null;
            return new JsonObject { ["@type"] = "AbsoluteTrigger", ["when"] = instant };
        }

        if (!PluginMailArgs.TryOptions(value, OffsetFields, true, out var o) || !PluginMailArgs.TryString(PluginMailArgs.Prop(o, "offset"), 64, out var offset)) return null;
        if (!OffsetShape().IsMatch(offset) || !offset.Any(char.IsAsciiDigit) || offset.EndsWith('T') || RecurrenceEngine.DurationSeconds(offset) is not { } seconds || Math.Abs(seconds) > MaxAlertOffsetSeconds) return null;
        var relative = "end";
        if (PluginMailArgs.Prop(o, "relativeTo") is { } relativeTo)
        {
            if (relativeTo is not { Kind: PluginValueKind.String } || relativeTo.StringValue is not ("due" or "start")) return null;
            // JSCalendar names a task's due its "end"
            relative = string.Equals(relativeTo.StringValue, "start", StringComparison.Ordinal) ? "start" : "end";
        }

        return new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = offset, ["relativeTo"] = relative };
    }

    /// <summary>linksOf / readHref (:688-735).</summary>
    private static JsonNode? ReadLink(PluginValue entry)
    {
        if (!PluginMailArgs.TryOptions(entry, LinkFields, true, out var l) || l is null) return null;
        if (!PluginMailArgs.TryString(PluginMailArgs.Prop(l, "href"), MaxHref, out var href) || !HrefOk(href)) return null;
        var link = new JsonObject { ["@type"] = "Link", ["href"] = href };
        if (PluginMailArgs.Prop(l, "rel") is { } rel)
        {
            if (!PluginMailArgs.TryString(rel, 64, out var relation) || !RelationName().IsMatch(relation)) return null;
            link["rel"] = relation;
        }

        if (PluginMailArgs.Prop(l, "title") is { } title)
        {
            if (!PluginMailArgs.TryString(title, 100_000, out var text) || (text = text.Trim()).Length > MaxLinkTitle) return null;
            if (text.Length > 0) link["title"] = text;
        }

        return link;
    }

    private static bool HrefOk(string href)
    {
        if (href.Length == 0 || href.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')) return false;
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            if (href.Contains('?', StringComparison.Ordinal) || href.Contains('#', StringComparison.Ordinal)) return false;
            try { return RecipientText.IsValidEmail(Uri.UnescapeDataString(href[7..])); }
            catch (UriFormatException) { return false; }
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;
    }

    /// <summary>relatedToOf (:737-757): { relation?: up to four lower-case relation types }.</summary>
    private static JsonNode? ReadRelation(PluginValue entry)
    {
        if (!PluginMailArgs.TryOptions(entry, RelationFields, true, out var r) || r is null) return null;
        var relation = new JsonObject { ["@type"] = "Relation" };
        if (PluginMailArgs.Prop(r, "relation") is not { } types) return relation;
        if (types.Kind != PluginValueKind.Array || types.Count > MaxRelationsPerUid) return null;
        var map = new JsonObject();
        for (var i = 0; i < types.Count; i++)
        {
            if (types.Item(i) is not { Kind: PluginValueKind.String } type || !RelationType().IsMatch(type.StringValue)) return null;
            map[type.StringValue] = true;
        }

        if (map.Count > 0) relation["relation"] = map;
        return relation;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex MemberId();

    [GeneratedRegex(@"^-?P(?:[0-9]+W|(?:[0-9]+D)?(?:T(?:[0-9]+H)?(?:[0-9]+M)?(?:[0-9]+S)?)?)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex OffsetShape();

    [GeneratedRegex("^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex RelationName();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex RelationType();

    [GeneratedRegex("^(?:<[!-;=?-~]+>|[!-;=?-~]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex RelatedUid();
}
