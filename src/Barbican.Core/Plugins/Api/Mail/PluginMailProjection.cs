// Mirrors: Bulwark lib/plugin-projection.ts:19-110 (plainTextFromEmail, emailToReadView, emailSummary), lib/plugin-types.ts:433-532
// (EmailReadView, EmailSummary, MailboxView), lib/plugin-sandbox/host-api-mail.ts:73-84 (mailboxToView), lib/jmap/server-keywords.ts and
// plugin-sandbox/assistant/b.projection.ts:17-70 (cid / alt of inline images). Every field is named here: nothing is copied by reflection,
// so a field added to a Barbican model never reaches a plugin. `auth` (parsed Authentication-Results) is deliberately not produced.
using System.Text.RegularExpressions;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal static partial class PluginMailProjection
{
    /// <summary>A message and its source can be megabytes; threads hold up to fifty of them.</summary>
    internal static readonly PluginValueLimits Limits = new(MaxWireBytes: 96 * 1024 * 1024, MaxNodes: 400_000, MaxStringBytes: 8 * 1024 * 1024);

    internal static readonly IReadOnlyDictionary<string, BodyValue> EmptyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    internal static IReadOnlyList<string> TrueKeys(IReadOnlyDictionary<string, bool>? map) => map is null ? [] : [.. map.Where(x => x.Value).Select(x => x.Key)];

    /// <summary>Keywords somebody applied: Stalwart's attachment keywords only restate hasAttachment (server-keywords.ts:13).</summary>
    internal static IReadOnlyList<string> UserKeywords(Keywords keywords) =>
        [.. keywords.Map.Where(x => x.Value && !IsServerSet(x.Key)).Select(x => x.Key)];

    internal static bool IsServerSet(string keyword) =>
        string.Equals(keyword, "$hasattachment", StringComparison.OrdinalIgnoreCase) || string.Equals(keyword, "$hasnoattachment", StringComparison.OrdinalIgnoreCase);

    internal static int Summary(PluginValueBuilder b, EmailSummary e, string accountId) => b.Object(
        ("id", b.String(e.Id)), ("accountId", b.String(accountId)), ("threadId", Optional(b, e.ThreadId)),
        ("mailboxIds", Strings(b, TrueKeys(e.MailboxIds))), ("from", Addresses(b, (e.From ?? []))), ("to", Addresses(b, (e.To ?? []))),
        ("subject", b.String(e.Subject ?? "")), ("receivedAt", b.String(Time(e.ReceivedAt))), ("preview", b.String(e.Preview ?? "")),
        ("keywords", Strings(b, TrueKeys(e.Keywords.Map))), ("hasAttachment", b.Boolean(e.HasAttachment)), ("size", b.Number(Math.Max(0, e.Size))));

    internal static int ReadView(PluginValueBuilder b, EmailBody e, string accountId)
    {
        var alts = CidAltTexts(e);
        var attachments = (e.Attachments ?? []).Where(a => !string.IsNullOrEmpty(a.BlobId)).Select(a => Attachment(b, a, alts)).ToArray();
        var fields = new List<(string, int)>
        {
            ("id", b.String(e.Id)), ("accountId", b.String(accountId)), ("threadId", Optional(b, e.ThreadId)),
            ("mailboxIds", Strings(b, TrueKeys(e.MailboxIds))), ("from", Addresses(b, (e.From ?? []))), ("to", Addresses(b, (e.To ?? []))), ("cc", Addresses(b, (e.Cc ?? []))),
            ("subject", b.String(e.Subject ?? "")), ("receivedAt", b.String(Time(e.ReceivedAt))),
            ("isRead", b.Boolean(e.Keywords.Seen)), ("isFlagged", b.Boolean(e.Keywords.Flagged)), ("hasAttachment", b.Boolean(e.HasAttachment)),
            ("attachments", b.Array(attachments)), ("preview", b.String(e.Preview ?? "")), ("text", b.String(PlainText(e))),
        };
        if ((e.Headers ?? []).Count > 0) fields.Add(("headers", Headers(b, (e.Headers ?? []))));
        fields.Add(("source", b.String(PluginEmailSource.Generate(e))));
        fields.Add(("keywords", Strings(b, UserKeywords(e.Keywords))));
        return b.Object([.. fields]);
    }

    internal static int Mailbox(PluginValueBuilder b, Mailbox m) => b.Object(
        ("id", b.String(m.Id)), ("name", b.String(m.Name)),
        ("role", m.Role == MailboxRole.None ? b.Null() : b.String(m.Role.ToString().ToLowerInvariant())),
        ("parentId", m.ParentId is null ? b.Null() : b.String(m.ParentId)),
        ("totalEmails", b.Number(m.TotalEmails)), ("unreadEmails", b.Number(m.UnreadEmails)));

    /// <summary>Bulwark plainTextFromEmail: each body part by its own type (HTML converted, never passed on), else the preview.</summary>
    internal static string PlainText(EmailBody e) =>
        FirstNonEmpty(Collect(e, (e.TextBody ?? []), "text/plain"), Collect(e, (e.HtmlBody ?? []), "text/html"), (e.Preview ?? "").Trim());

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";

    private static string Collect(EmailBody e, IReadOnlyList<BodyPart> parts, string untyped)
    {
        var pieces = new List<string>();
        foreach (var part in parts)
        {
            if (part.PartId is null || !(e.BodyValues ?? EmptyValues).TryGetValue(part.PartId, out var value) || value.Value.Length == 0) continue;
            var text = string.Equals(part.Type ?? untyped, "text/html", StringComparison.OrdinalIgnoreCase) ? MailHtml.HtmlToText(value.Value) : value.Value;
            if (!string.IsNullOrWhiteSpace(text)) pieces.Add(text);
        }
        return string.Join('\n', pieces).Trim();
    }

    private static int Attachment(PluginValueBuilder b, Attachment a, Dictionary<string, string> alts)
    {
        var fields = new List<(string, int)>
        {
            ("blobId", b.String(a.BlobId!)), ("name", b.String(a.Name ?? "")),
            ("type", b.String(string.IsNullOrEmpty(a.Type) ? "application/octet-stream" : a.Type)), ("size", b.Number(Math.Max(0, a.Size))),
            ("inline", b.Boolean(string.Equals(a.Disposition, "inline", StringComparison.Ordinal) || !string.IsNullOrEmpty(a.Cid))),
        };
        if (!string.IsNullOrEmpty(a.Cid))
        {
            var cid = a.Cid.Trim('<', '>');
            fields.Add(("cid", b.String(cid)));
            if (alts.TryGetValue(NormaliseCid(cid), out var alt)) fields.Add(("alt", b.String(alt)));
        }
        return b.Object([.. fields]);
    }

    private static int Headers(PluginValueBuilder b, IReadOnlyList<EmailHeader> headers) => b.Object([.. headers.GroupBy(h => h.Name, StringComparer.Ordinal)
        .Select(g => (g.Key, g.Count() == 1 ? b.String(g.First().Value) : b.Array([.. g.Select(h => b.String(h.Value))])))]);

    private static int Addresses(PluginValueBuilder b, IReadOnlyList<EmailAddress> list) =>
        b.Array([.. list.Select(a => b.Object(("name", b.String(a.Name ?? "")), ("email", b.String(a.Email))))]);

    private static int Strings(PluginValueBuilder b, IReadOnlyList<string> values) => b.Array([.. values.Select(b.String)]);

    private static int Optional(PluginValueBuilder b, string? value) => value is null ? b.Undefined() : b.String(value);

    private static string Time(DateTimeOffset? value) => value is { } v ? v.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) : "";

    // ---- inline image alt text (b.projection.ts:17-60) ----

    private static string NormaliseCid(string value)
    {
        var id = value.Trim();
        if (id.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) id = id[4..];
        id = id.Trim('<', '>');
        try { id = Uri.UnescapeDataString(id); }
        catch (UriFormatException) { /* keep it as written */ }
        return id.ToLowerInvariant();
    }

    private static Dictionary<string, string> CidAltTexts(EmailBody e)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (e.HtmlBody ?? []))
        {
            if (part.PartId is null || !(e.BodyValues ?? EmptyValues).TryGetValue(part.PartId, out var html)) continue;
            foreach (Match tag in ImgTag().Matches(html.Value))
            {
                var src = Attribute(tag.Value, "src");
                if (src is null || !src.TrimStart().StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) continue;
                var alt = WhiteSpace().Replace(Attribute(tag.Value, "alt") ?? Attribute(tag.Value, "title") ?? "", " ").Trim();
                var cid = NormaliseCid(src);
                if (alt.Length > 0 && !output.ContainsKey(cid)) output[cid] = alt.Length > 500 ? alt[..500] : alt;
            }
        }
        return output;
    }

    private static string? Attribute(string tag, string name)
    {
        var m = new Regex($@"\s{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout).Match(tag);
        if (!m.Success) return null;
        var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return System.Net.WebUtility.HtmlDecode(raw);
    }

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex WhiteSpace();
}
