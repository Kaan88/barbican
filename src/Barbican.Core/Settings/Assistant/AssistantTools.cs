// Mirrors: see IAssistantTools; Rust agent.rs:48-150 (tool specs), :152-440 (exec), describe/label. B-AI-007, B-AI-008, B-AI-009. The tools call typed services (IMailService through the session, IMailWriter, IVacationService, IFilterService, ISettingValues), never raw JMAP; mail text goes back to the model inside <email> tags (B-AI-004).
// D-M6-A12: the tool set uses existing typed services. Composer opening is an optional UI bridge; calendar and contact tools are capability-filtered in the session.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Compose;
using Barbican.Core.Contacts;
using Barbican.Core.Editor;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Mail.Write;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantTools"/>.</summary>
public sealed partial class AssistantTools(
    IAccountManager accounts,
    IMailWriterFactory writers,
    ISettingValues values,
    IVacationService vacation,
    IFilterService filters,
    IAssistantSettings settings,
    IComposeSeeder seeder,
    IMailSender sender,
    IContactStore contacts,
    IContactWriter contactWriter,
    ICalendarStore calendars,
    ICalendarWriter calendarWriter,
    IAssistantComposer? draftOpener = null) : IAssistantTools
{
    private static readonly IReadOnlyList<AssistantToolSpec> All = BuildSpecs();
    private static readonly string[] TextMimeParts = ["json", "xml", "calendar", "vcard", "csv"];
    private static readonly string[] TextExtensions = [".txt", ".csv", ".ics", ".vcf", ".json", ".xml", ".md", ".html", ".htm", ".log"];

    /// <inheritdoc/>
    public IReadOnlyList<AssistantToolSpec> Specs(AssistantActionMode mode) => mode == AssistantActionMode.Off ? [.. All.Where(t => !t.Writes)] : All;

    /// <inheritdoc/>
    public async Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = accounts.SessionFor(accountKey);
        if (session is null)
        {
            return "Error: the account is not signed in.";
        }

        try
        {
            var text = name switch
            {
                "search_mail" => await SearchAsync(session, args, ct).ConfigureAwait(false),
                "read_attachment" => await ReadAttachmentAsync(session, args, ct).ConfigureAwait(false),
                "read_message" => await ReadMessageAsync(session, Str(args, "id"), ct).ConfigureAwait(false),
                "read_thread" => await ReadThreadAsync(session, Str(args, "id"), ct).ConfigureAwait(false),
                "list_folders" => await ListFoldersAsync(session, ct).ConfigureAwait(false),
                "move_messages" => await MoveAsync(session, args, null, ct).ConfigureAwait(false),
                "archive_messages" => await MoveAsync(session, args, "archive", ct).ConfigureAwait(false),
                "delete_messages" => await MoveAsync(session, args, "trash", ct).ConfigureAwait(false),
                "mark_messages" => await MarkAsync(session, args, ct).ConfigureAwait(false),
                "check_calendar" => await CheckCalendarAsync(session, accountKey, args, ct).ConfigureAwait(false),
                "create_event" => await CreateEventAsync(session, accountKey, args, ct).ConfigureAwait(false),
                "find_contact" => await FindContactsAsync(session, accountKey, args, ct).ConfigureAwait(false),
                "create_contact" => await CreateContactAsync(session, accountKey, args, ct).ConfigureAwait(false),
                "draft_email" => await OpenDraftAsync(accountKey, args, ct).ConfigureAwait(false),
                "send_email" => await SendAsync(accountKey, args, ct).ConfigureAwait(false),
                "tag_messages" => await TagAsync(session, args, ct).ConfigureAwait(false),
                "create_folder" => await CreateFolderAsync(session, args, ct).ConfigureAwait(false),
                "get_vacation" => await GetVacationAsync(accountKey, ct).ConfigureAwait(false),
                "set_vacation" => await SetVacationAsync(accountKey, args, ct).ConfigureAwait(false),
                "add_filter_rule" => await ChangeFilterAsync(session, accountKey, args, true, ct).ConfigureAwait(false),
                "remove_filter_rule" => await ChangeFilterAsync(session, accountKey, args, false, ct).ConfigureAwait(false),
                "list_filters" => await ListFiltersAsync(accountKey, ct).ConfigureAwait(false),
                "change_setting" => await ChangeSettingAsync(args, ct).ConfigureAwait(false),
                "remember" => await RememberAsync(accountKey, Str(args, "note"), ct).ConfigureAwait(false),
                _ => "Error: unknown tool " + name,
            };
            return text.Length > AssistantLoop.MaxToolResult ? text[..AssistantLoop.MaxToolResult] : text;
        }
        catch (PortPendingException)
        {
            // a service of another stream that has not landed: the model reads it and tells the user
            return "Error: this is not available yet.";
        }
    }

    private static string Str(JsonObject a, string key) => a[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool? Flag(JsonObject a, string key) => a[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static string Fail(string text) => "Error: " + text;

    private static JsonObject Schema(string properties, params string[] required) =>
        new() { ["type"] = "object", ["properties"] = JsonNode.Parse(properties), ["required"] = new JsonArray([.. required.Select(r => (JsonNode?)r)]) };

    private static List<AssistantToolSpec> BuildSpecs()
    {
        const string ids = """{"type": "array", "items": {"type": "string"}, "description": "Email ids from search_mail or read_message"}""";
        return
        [
            new("search_mail", "Search the user's mail. All filters optional. Returns id, date, sender, subject, flags and a preview for each match, newest first.",
                Schema("""{"text": {"type": "string"}, "from": {"type": "string"}, "to": {"type": "string"}, "subject": {"type": "string"}, "folder": {"type": "string", "description": "Folder name or role (inbox, sent, archive, trash, junk, drafts)"}, "unread": {"type": "boolean"}, "flagged": {"type": "boolean"}, "has_attachment": {"type": "boolean"}, "after": {"type": "string", "description": "ISO date, e.g. 2026-11-01"}, "before": {"type": "string"}, "limit": {"type": "integer", "description": "Default 25, max 100"}}"""), false),
            new("read_attachment", "Read a text attachment by message id and optional filename.", Schema("""{"id":{"type":"string"},"name":{"type":"string"}}""", "id"), false),
            new("read_message", "Read one email in full (headers, text, attachment names). Use id \"current\" for the message open in the reader.", Schema("""{"id": {"type": "string"}}""", "id"), false),
            new("read_thread", "Read the whole conversation an email belongs to (up to the last 20 messages). Use id \"current\" for the open message.", Schema("""{"id": {"type": "string"}}""", "id"), false),
            new("list_folders", "List mail folders with ids, roles and unread/total counts.", Schema("{}"), false),
            new("move_messages", "Move emails to a folder (by name or role).", Schema($$$"""{"ids": {{{ids}}}, "folder": {"type": "string"}}""", "ids", "folder"), true),
            new("archive_messages", "Move emails to Archive.", Schema($$$"""{"ids": {{{ids}}}}""", "ids"), true),
            new("delete_messages", "Move emails to Trash.", Schema($$$"""{"ids": {{{ids}}}}""", "ids"), true),
            new("mark_messages", "Mark emails read/unread/flagged/unflagged.", Schema($$$"""{"ids": {{{ids}}}, "mark": {"type": "string", "enum": ["read", "unread", "flagged", "unflagged"]}}""", "ids", "mark"), true),
            new("check_calendar", "List calendar events between two dates (end exclusive).", Schema("""{"after":{"type":"string"},"before":{"type":"string"}}""", "after", "before"), false),
            new("create_event", "Create an event in the default calendar using a local start and duration in minutes.", Schema("""{"title":{"type":"string"},"start":{"type":"string"},"duration_minutes":{"type":"integer"},"location":{"type":"string"},"description":{"type":"string"}}""", "title", "start"), true),
            new("find_contact", "Find contacts by name or email in this account.", Schema("""{"query":{"type":"string"}}""", "query"), false),
            new("create_contact", "Create a contact in the default address book.", Schema("""{"name":{"type":"string"},"email":{"type":"string"},"phone":{"type":"string"},"company":{"type":"string"}}""", "name"), true),
            new("draft_email", "Open a draft in the composer for review.", Schema("""{"to":{"type":"string"},"cc":{"type":"string"},"bcc":{"type":"string"},"subject":{"type":"string"},"body":{"type":"string"},"reply_to_id":{"type":"string"}}""", "body"), false),
            new("send_email", "Send an email after the user confirms its contents.", Schema("""{"to":{"type":"string"},"cc":{"type":"string"},"bcc":{"type":"string"},"subject":{"type":"string"},"body":{"type":"string"},"reply_to_id":{"type":"string"}}""", "to", "subject", "body"), true),
            new("tag_messages", "Add or remove an existing tag on emails.", Schema($$$"""{"ids": {{{ids}}}, "tag": {"type": "string"}, "remove": {"type": "boolean"}}""", "ids", "tag"), true),
            new("create_folder", "Create a mail folder, optionally inside a parent folder.", Schema("""{"name": {"type": "string"}, "parent": {"type": "string"}}""", "name"), true),
            new("get_vacation", "Read the out-of-office (vacation) auto-reply.", Schema("{}"), false),
            new("set_vacation", "Turn the out-of-office auto-reply on or off and set its text and dates.", Schema("""{"enabled": {"type": "boolean"}, "subject": {"type": "string"}, "body": {"type": "string"}, "from_date": {"type": "string", "description": "ISO date"}, "to_date": {"type": "string", "description": "ISO date"}}""", "enabled"), true),
            new("add_filter_rule", "Add a mail filter rule.", Schema("""{"name":{"type":"string"},"field":{"type":"string"},"comparator":{"type":"string"},"value":{"type":"string"},"action":{"type":"string"},"action_value":{"type":"string"},"stop":{"type":"boolean"}}""", "name", "field", "action"), true),
            new("remove_filter_rule", "Remove a mail filter rule by name.", Schema("""{"name":{"type":"string"}}""", "name"), true),
            new("list_filters", "Show the user's mail filter rules (the active Sieve script).", Schema("{}"), false),
            new("change_setting", "Change one app setting by its Bulwark key, e.g. density, theme (light|dark|system), showPreview (bool), timeFormat (12h|24h), emailsPerPage.", Schema("""{"key": {"type": "string"}, "value": {}}""", "key", "value"), true),
            new("remember", "Save a short note about the user's preferences for future chats.", Schema("""{"note": {"type": "string"}}""", "note"), false),
        ];
    }

    private static async Task<(string? Id, string? Error)> FolderAsync(IAccountSession session, string name, CancellationToken ct)
    {
        var tree = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        if (!tree.IsOk)
        {
            return (null, tree.Error!.Message);
        }

        var n = name.Trim().ToLowerInvariant();
        n = n switch { "spam" => "junk", "deleted" or "bin" => "trash", _ => n };
        var found = tree.Value!.All.FirstOrDefault(m => string.Equals(m.Role.ToString(), n, StringComparison.OrdinalIgnoreCase) && m.Role != MailboxRole.None)
            ?? tree.Value.All.FirstOrDefault(m => string.Equals(m.Id, name, StringComparison.Ordinal) || string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase));
        return found is null ? (null, $"No folder named \"{name}\".") : (found.Id, null);
    }

    private static string Date(DateTimeOffset? t) => t?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Who(IReadOnlyList<EmailAddress> list) => string.Join(", ", list.Select(a => string.IsNullOrEmpty(a.Name) ? a.Email : $"{a.Name} <{a.Email}>"));

    private static string Isoday(string day) => day.Length == 10 ? day + "T00:00:00Z" : day;

#pragma warning disable MA0051 // The search filters and query are kept together for parity with the reference.
    private static async Task<string> SearchAsync(IAccountSession session, JsonObject a, CancellationToken ct)
    {
        var conditions = new List<JsonObject>();
        foreach (var key in new[] { "text", "from", "to", "subject" })
        {
            if (Str(a, key) is { Length: > 0 } v)
            {
                conditions.Add(new JsonObject { [key] = v });
            }
        }

        if (Str(a, "folder") is { Length: > 0 } folder)
        {
            var f = await FolderAsync(session, folder, ct).ConfigureAwait(false);
            if (f.Id is null)
            {
                return Fail(f.Error!);
            }

            conditions.Add(new JsonObject { ["inMailbox"] = f.Id });
        }

        switch (Flag(a, "unread"))
        {
            case true:
                conditions.Add(new JsonObject { ["notKeyword"] = "$seen" });
                break;
            case false:
                conditions.Add(new JsonObject { ["hasKeyword"] = "$seen" });
                break;
            default:
                break;
        }

        if (Flag(a, "flagged") == true)
        {
            conditions.Add(new JsonObject { ["hasKeyword"] = "$flagged" });
        }

        if (Flag(a, "has_attachment") == true)
        {
            conditions.Add(new JsonObject { ["hasAttachment"] = true });
        }

        foreach (var key in new[] { "after", "before" })
        {
            if (Str(a, key) is { Length: > 0 } d)
            {
                conditions.Add(new JsonObject { [key] = Isoday(d) });
            }
        }

        var limit = a["limit"] is JsonValue lv && lv.TryGetValue<int>(out var l) ? Math.Clamp(l, 1, 100) : 25;
        var filter = conditions.Count switch { 0 => [], 1 => conditions[0], _ => new JsonObject { ["operator"] = "AND", ["conditions"] = new JsonArray([.. conditions.Select(c => (JsonNode?)c)]) } };
        var page = await session.Mail.QueryAsync(new ListQuery { Filter = filter, PageSize = limit, CollapseThreads = false }, null, ct).ConfigureAwait(false);
        if (!page.IsOk)
        {
            return Fail(page.Error!.Message);
        }

        if (page.Value!.Items.Count == 0)
        {
            return "No messages found.";
        }

        var sb = new StringBuilder();
        foreach (var m in page.Value.Items.Take(limit))
        {
            var flags = (m.Keywords.Seen ? string.Empty : "unread ") + (m.Keywords.Flagged ? "flagged " : string.Empty) + (m.HasAttachment ? "attachment" : string.Empty);
            _ = sb.Append(CultureInfo.InvariantCulture, $"{m.Id} | {Date(m.ReceivedAt)} | {Who(m.From)} | {m.Subject} | {flags.Trim()} | {m.Preview}\n");
        }

        // the sender, subject and preview are mail text: untrusted data (B-AI-004)
        return AssistantPrompt.WrapEmail(sb.ToString().TrimEnd());
    }

#pragma warning restore MA0051
    private static string TextOf(EmailBody m)
    {
        string Join(IReadOnlyList<BodyPart> parts) => string.Join('\n', parts.Select(p => p.PartId is { } id && m.BodyValues.TryGetValue(id, out var v) ? v.Value : string.Empty));
        var text = Join(m.TextBody);
        if (text.Trim().Length > 0)
        {
            return text;
        }

        var html = Join(m.HtmlBody);
        return Entities().Replace(Tags().Replace(html, " "), " ").Trim();
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Tags();

    [GeneratedRegex("&[a-zA-Z#0-9]+;", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Entities();

    private static async Task<string> ReadAttachmentAsync(IAccountSession session, JsonObject args, CancellationToken ct)
    {
        var message = await session.Mail.GetBodyAsync(Str(args, "id"), new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!message.IsOk)
        {
            return Fail(message.Error!.Message);
        }

        var name = Str(args, "name");
        var attachment = message.Value!.Attachments.FirstOrDefault(a => name.Length > 0 && a.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true) ?? (message.Value.Attachments.Count > 0 ? message.Value.Attachments[0] : null);
        var type = attachment?.Type ?? "application/octet-stream";
        var filename = attachment?.Name ?? string.Empty;
        var textType = type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || TextMimeParts.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)) || TextExtensions.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (attachment?.BlobId is not { Length: > 0 } blob || !textType)
        {
            return Fail("No matching text attachment.");
        }

        var downloaded = await session.Client.DownloadAsync(session.Client.Session.MailAccountId, blob, filename, type, ct).ConfigureAwait(false);
        if (!downloaded.IsOk)
        {
            return Fail(downloaded.Error!.Message);
        }

        using var stream = downloaded.Value!;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[AssistantLoop.MaxToolResult - 128];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
        return AssistantPrompt.WrapEmail(new string(buffer, 0, count));
    }

    private static async Task<string> ReadMessageAsync(IAccountSession session, string id, CancellationToken ct)
    {
        if (id.Length == 0)
        {
            return Fail("No message is open in the reader.");
        }

        var got = await session.Mail.GetBodyAsync(id, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Fail(got.Error!.Message);
        }

        var m = got.Value!;
        var names = string.Join(", ", m.Attachments.Select(x => x.Name).Where(n => !string.IsNullOrEmpty(n)));
        var text = $"From: {Who(m.From)}\nTo: {Who(m.To)}\nCc: {Who(m.Cc)}\nDate: {Date(m.ReceivedAt ?? m.SentAt)}\nSubject: {m.Subject}\nAttachments: {(names.Length == 0 ? "none" : names)}\n\n{TextOf(m)}";
        return AssistantPrompt.WrapEmail(text);
    }

    private static async Task<string> ReadThreadAsync(IAccountSession session, string id, CancellationToken ct)
    {
        if (id.Length == 0)
        {
            return Fail("No message is open in the reader.");
        }

        var one = await session.Mail.GetBodyAsync(id, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!one.IsOk || one.Value!.ThreadId is not { } threadId)
        {
            return one.IsOk ? ReadMessageText(one.Value!) : Fail(one.Error!.Message);
        }

        var thread = await session.Mail.GetThreadAsync(threadId, ct).ConfigureAwait(false);
        if (!thread.IsOk)
        {
            return Fail(thread.Error!.Message);
        }

        var sb = new StringBuilder();
        foreach (var e in thread.Value!.Emails.TakeLast(20))
        {
            var body = await session.Mail.GetBodyAsync(e.Id, new BodyFetchOptions(), ct).ConfigureAwait(false);
            _ = sb.Append(body.IsOk ? ReadMessageText(body.Value!) : $"{e.Id}: unavailable").Append("\n---\n");
        }

        return AssistantPrompt.WrapEmail(sb.ToString().TrimEnd());
    }

    private static string ReadMessageText(EmailBody m) => $"From: {Who(m.From)}\nDate: {Date(m.ReceivedAt ?? m.SentAt)}\nSubject: {m.Subject}\n\n{TextOf(m)}";

    private static async Task<string> ListFoldersAsync(IAccountSession session, CancellationToken ct)
    {
        var tree = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        return tree.IsOk
            ? string.Join('\n', tree.Value!.All.Select(m => $"{m.Id} | {m.Name} | {(m.Role == MailboxRole.None ? string.Empty : m.Role.ToString().ToLowerInvariant())} | {m.UnreadEmails} unread of {m.TotalEmails}"))
            : Fail(tree.Error!.Message);
    }

    private static List<string> IdsOf(JsonObject a) =>
        [.. (a["ids"] as JsonArray ?? []).Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().Where(s => s.Length > 0).Take(200)];

    private static string Report(Result<SetReport> r) =>
        r.IsOk ? $"Done: {r.Value!.Updated.Count} updated{(r.Value.Refused.Count > 0 ? $", {r.Value.Refused.Count} failed" : string.Empty)}." : Fail(r.Error!.Message);

    private async Task<string> MoveAsync(IAccountSession session, JsonObject a, string? role, CancellationToken ct)
    {
        var ids = IdsOf(a);
        if (ids.Count == 0)
        {
            return Fail("No message ids given.");
        }

        var target = await FolderAsync(session, role ?? Str(a, "folder"), ct).ConfigureAwait(false);
        return target.Id is null ? Fail(target.Error!) : Report(await writers.ForSession(session).MoveAsync(ids, null, target.Id, ct).ConfigureAwait(false));
    }

    private async Task<string> MarkAsync(IAccountSession session, JsonObject a, CancellationToken ct)
    {
        var ids = IdsOf(a);
        if (ids.Count == 0)
        {
            return Fail("No message ids given.");
        }

        IReadOnlyDictionary<string, bool>? patch = Str(a, "mark") switch
        {
            "read" => new Dictionary<string, bool>(StringComparer.Ordinal) { ["$seen"] = true },
            "unread" => new Dictionary<string, bool>(StringComparer.Ordinal) { ["$seen"] = false },
            "flagged" => new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = true },
            "unflagged" => new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = false },
            _ => null,
        };
        return patch is null ? Fail("mark must be read, unread, flagged or unflagged.") : Report(await writers.ForSession(session).UpdateKeywordsAsync(ids, patch, ct).ConfigureAwait(false));
    }

    private TimeZoneInfo CalendarZone()
    {
        var configured = values.Stored("timeZone") is JsonValue setting && setting.TryGetValue<string>(out var pref) ? pref : "auto";
        if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(configured);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Match CalendarSettings.Zone: invalid preference falls back to the machine zone.
            }
        }

        return TimeZoneInfo.Local;
    }

    private async Task<string> CheckCalendarAsync(IAccountSession session, string accountKey, JsonObject args, CancellationToken ct)
    {
        if (!session.Client.Session.Has("urn:ietf:params:jmap:calendars"))
        {
            return Fail("Calendars are not available on this account.");
        }

        var from = Day(Str(args, "after"));
        var to = Day(Str(args, "before"));
        if (from is null || to is null || to <= from || to.Value.DayNumber - from.Value.DayNumber > 366)
        {
            return Fail("Give a valid date range of at most one year.");
        }

        var loaded = await calendars.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Fail(loaded.Error!.Message);
        }

        var occurrences = CalendarOccurrences.In(loaded.Value!.Events, from.Value, to.Value, CalendarZone()).Take(100);
        return AssistantPrompt.WrapEmail(string.Join("\n", occurrences.Select(o => $"{o.Start:yyyy-MM-dd HH:mm} - {o.End:yyyy-MM-dd HH:mm} | {o.Title} | {o.Location}")));
    }

    private async Task<string> CreateEventAsync(IAccountSession session, string accountKey, JsonObject args, CancellationToken ct)
    {
        if (!session.Client.Session.Has("urn:ietf:params:jmap:calendars"))
        {
            return Fail("Calendars are not available on this account.");
        }

        var title = Str(args, "title").Trim();
        var start = Str(args, "start");
        var minutes = args["duration_minutes"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 60;
        if (title.Length == 0 || minutes is <= 0 or > 10080 || !DateTime.TryParseExact(start, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Fail("Give a title, local start and positive duration of at most one week.");
        }

        var loaded = await calendars.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Fail(loaded.Error!.Message);
        }

        var primary = session.Client.Session.AccountFor("urn:ietf:params:jmap:calendars");
        var own = loaded.Value!.Calendars.Where(c => (c["_acc"] is null || string.Equals((string?)c["_acc"], primary, StringComparison.Ordinal)) && (bool?)c["myRights"]?["mayAddItems"] != false).ToList();
        var selected = own.FirstOrDefault(c => (bool?)c["isDefault"] == true) ?? (own.Count > 0 ? own[0] : null);
        var calendar = (string?)selected?["id"];
        if (string.IsNullOrEmpty(calendar))
        {
            return Fail("No calendar found.");
        }

        var zoneId = CalendarZone().Id;
        var zone = TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var iana) ? iana : zoneId;
        var data = new JsonObject { ["@type"] = "Event", ["title"] = title, ["start"] = start, ["timeZone"] = zone, ["duration"] = System.Xml.XmlConvert.ToString(TimeSpan.FromMinutes(minutes)) };
        if (Str(args, "location") is { Length: > 0 } location)
        {
            data["locations"] = new JsonObject { ["l1"] = new JsonObject { ["@type"] = "Location", ["name"] = location } };
        }

        if (Str(args, "description") is { Length: > 0 } description)
        {
            data["description"] = description;
        }

        var result = await calendarWriter.CreateAsync(accountKey, calendar, data, ct).ConfigureAwait(false);
        return result.IsOk ? "Event created." : Fail(result.Error!.Message);
    }

    private async Task<string> FindContactsAsync(IAccountSession session, string accountKey, JsonObject args, CancellationToken ct)
    {
        if (!session.Client.Session.Has(ContactOptions.Capability))
        {
            return Fail("Contacts are not available on this account.");
        }

        var query = Str(args, "query").Trim();
        if (query.Length == 0)
        {
            return Fail("Give a name or email to search for.");
        }

        var loaded = await contacts.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Fail(loaded.Error!.Message);
        }

        var matches = loaded.Value!.Cards.Where(c => c.ToJsonString().Contains(query, StringComparison.OrdinalIgnoreCase)).Take(25).Select(c => c.ToJsonString());
        return AssistantPrompt.WrapEmail(string.Join("\n", matches));
    }

    private async Task<string> CreateContactAsync(IAccountSession session, string accountKey, JsonObject args, CancellationToken ct)
    {
        if (!session.Client.Session.Has(ContactOptions.Capability))
        {
            return Fail("Contacts are not available on this account.");
        }

        var name = Str(args, "name").Trim();
        var email = Str(args, "email").Trim();
        if (name.Length == 0)
        {
            return Fail("Give a name.");
        }

        var card = new JsonObject { ["name"] = new JsonObject { ["full"] = name } };
        if (email.Length > 0) card["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = email } };
        if (Str(args, "phone") is { Length: > 0 } phone) card["phones"] = new JsonObject { ["p0"] = new JsonObject { ["number"] = phone } };
        if (Str(args, "company") is { Length: > 0 } company) card["organizations"] = new JsonObject { ["o0"] = new JsonObject { ["name"] = company } };
        var result = await contactWriter.CreateAsync(accountKey, null, card, ct).ConfigureAwait(false);
        return result.IsOk ? "Contact created." : Fail(result.Error!.Message);
    }

    private async Task<string> OpenDraftAsync(string accountKey, JsonObject args, CancellationToken ct)
    {
        if (draftOpener is null)
        {
            return Fail("The composer is unavailable.");
        }

        var reply = Str(args, "reply_to_id");
        var request = new ComposeRequest(accountKey, reply.Length == 0 ? ComposeKind.New : ComposeKind.Reply)
        {
            SourceEmailId = reply.Length == 0 ? null : reply,
            Prefill = new ComposePrefill { To = Str(args, "to"), Cc = Str(args, "cc"), Bcc = Str(args, "bcc"), Subject = Str(args, "subject"), Body = Str(args, "body") },
        };
        var opened = await draftOpener.OpenAsync(request, ct).ConfigureAwait(false);
        return opened.IsOk ? "The draft is open in the composer for review." : Fail(opened.Error!.Message);
    }

    private async Task<string> SendAsync(string accountKey, JsonObject args, CancellationToken ct)
    {
        var prefill = new ComposePrefill { To = Str(args, "to"), Cc = Str(args, "cc"), Bcc = Str(args, "bcc"), Subject = Str(args, "subject"), Body = Str(args, "body") };
        if (string.IsNullOrWhiteSpace(prefill.To) && string.IsNullOrWhiteSpace(prefill.Cc) && string.IsNullOrWhiteSpace(prefill.Bcc))
        {
            return Fail("Add at least one recipient.");
        }

        var reply = Str(args, "reply_to_id");
        var request = new ComposeRequest(accountKey, reply.Length == 0 ? ComposeKind.New : ComposeKind.Reply) { Prefill = prefill, SourceEmailId = reply.Length == 0 ? null : reply };
        var seeded = await seeder.SeedAsync(request, ct).ConfigureAwait(false);
        if (!seeded.IsOk)
        {
            return Fail(seeded.Error!.Message);
        }

        var seed = seeded.Value!;
        if (string.IsNullOrEmpty(seed.IdentityId))
        {
            return Fail("No sending identity on this account.");
        }

        var draft = new ComposeDraft { AccountId = accountKey, Kind = seed.Kind, IdentityId = seed.IdentityId, To = prefill.To, Cc = prefill.Cc, Bcc = prefill.Bcc, Subject = prefill.Subject, Body = Doc.FromText(prefill.Body), SourceEmailId = seed.SourceEmailId, InReplyTo = seed.InReplyTo, References = seed.References };
        var sent = await sender.SendAsync(draft, new SendOptions(), ct).ConfigureAwait(false);
        return sent.IsOk ? "Email sent." : Fail(sent.Error!.Message);
    }

    private async Task<string> TagAsync(IAccountSession session, JsonObject args, CancellationToken ct)
    {
        var ids = IdsOf(args);
        if (ids.Count == 0)
        {
            return Fail("No message ids given.");
        }

        var requested = Str(args, "tag").Trim();
        var tag = TagRules.Parse(values.Stored("emailKeywords")).FirstOrDefault(t =>
            string.Equals(t.Id, requested, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Label, requested, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return Fail("No such tag.");
        }

        var patch = new Dictionary<string, bool>(StringComparer.Ordinal) { ["$label:" + tag.Id] = Flag(args, "remove") != true };
        return Report(await writers.ForSession(session).UpdateKeywordsAsync(ids, patch, ct).ConfigureAwait(false));
    }

    private async Task<string> CreateFolderAsync(IAccountSession session, JsonObject a, CancellationToken ct)
    {
        var name = Str(a, "name").Trim();
        if (name.Length == 0)
        {
            return Fail("Give the folder a name.");
        }

        string? parent = null;
        if (Str(a, "parent") is { Length: > 0 } p)
        {
            var f = await FolderAsync(session, p, ct).ConfigureAwait(false);
            if (f.Id is null)
            {
                return Fail(f.Error!);
            }

            parent = f.Id;
        }

        var r = await writers.ForSession(session).CreateMailboxAsync(name, parent, null, ct).ConfigureAwait(false);
        return r.IsOk ? $"Folder \"{name}\" created." : Fail(r.Error!.Message);
    }

    private async Task<string> GetVacationAsync(string accountKey, CancellationToken ct)
    {
        var v = await vacation.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!v.IsOk)
        {
            return Fail(v.Error!.Message);
        }

        var s = v.Value!;
        return $"Out of office is {(s.Enabled ? "on" : "off")}. Subject: {s.Subject}. From {s.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "now"} to {s.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no end date"}.\n{s.TextBody}";
    }

    private async Task<string> SetVacationAsync(string accountKey, JsonObject a, CancellationToken ct)
    {
        var current = await vacation.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!current.IsOk)
        {
            return Fail(current.Error!.Message);
        }

        var s = current.Value!;
        var next = s with
        {
            Enabled = Flag(a, "enabled") ?? s.Enabled,
            Subject = a["subject"] is null ? s.Subject : Str(a, "subject"),
            TextBody = a["body"] is null ? s.TextBody : Str(a, "body"),
            From = a["from_date"] is null ? s.From : Day(Str(a, "from_date")),
            To = a["to_date"] is null ? s.To : Day(Str(a, "to_date")),
        };
        var saved = await vacation.SaveAsync(accountKey, next, ct).ConfigureAwait(false);
        return saved.IsOk ? $"Out of office is now {(saved.Value!.Enabled ? "on" : "off")}." : Fail(saved.Error!.Message);
    }

    private static DateOnly? Day(string text) =>
        text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private async Task<string> ChangeFilterAsync(IAccountSession session, string accountKey, JsonObject args, bool add, CancellationToken ct)
    {
        var loaded = await filters.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Fail(loaded.Error!.Message);
        }

        if (loaded.Value!.Locked)
        {
            return Fail("The filter script was edited by hand and cannot be changed here.");
        }

        var name = Str(args, "name").Trim();
        if (name.Length == 0)
        {
            return Fail("Give the rule a name.");
        }

        var rules = loaded.Value.Rules.Select(r => r.DeepClone()).ToList();
        if (add)
        {
            var folders = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
            if (!folders.IsOk)
            {
                return Fail(folders.Error!.Message);
            }

            var folderList = folders.Value!.All.Select(m => new FilterFolder(m.Id, m.Name)).ToList();
            var field = Str(args, "field");
            var comparator = Str(args, "comparator");
            var kind = Str(args, "action");
            if (!FilterRules.Fields.Any(f => string.Equals(f.Id, field, StringComparison.Ordinal)) || !FilterRules.ActionKinds(loaded.Value.Extensions).Any(a => string.Equals(a.Id, kind, StringComparison.Ordinal)))
            {
                return Fail("Unknown filter field or action.");
            }

            var value = Str(args, "action_value");
            var folder = kind is "move" or "copy" ? folderList.FirstOrDefault(f => string.Equals(f.Id, value, StringComparison.Ordinal) || string.Equals(f.Path, value, StringComparison.OrdinalIgnoreCase)) : null;
            var condition = new FilterCond(field, comparator.Length == 0 ? "contains" : comparator, Str(args, "value"), "");
            var action = new FilterAct(kind, value, folder?.Id ?? string.Empty, false);
            var built = FilterRules.BuildRule(null, null, name, false, [condition], [action], folderList, Flag(args, "stop") == true, false, "", "");
            if (built.Rule is null)
            {
                return Fail(built.Error ?? "The rule is invalid.");
            }

            rules.Add(built.Rule);
        }
        else if (rules.RemoveAll(r => string.Equals((string?)r["name"], name, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return Fail("No rule with that name.");
        }

        var saved = await filters.SaveAsync(accountKey, rules, ct).ConfigureAwait(false);
        return saved.IsOk ? (add ? "Rule added." : "Rule removed.") : Fail(saved.Error!.Message);
    }

    private async Task<string> ListFiltersAsync(string accountKey, CancellationToken ct)
    {
        var state = await filters.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!state.IsOk)
        {
            return Fail(state.Error!.Message);
        }

        return state.Value!.Rules.Count == 0 ? "There are no filter rules." : string.Join('\n', state.Value.Rules.Select((r, i) => $"{i + 1}. {(string?)r["name"] ?? "(unnamed)"}"));
    }

    private async Task<string> ChangeSettingAsync(JsonObject a, CancellationToken ct)
    {
        var key = Str(a, "key");
        var row = SettingsCatalog.RowOfKey(key);
        if (key.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("That setting cannot be changed by the assistant.");
        }

        if (row is null || row.Kind is SettingRowKind.Info or SettingRowKind.Button or SettingRowKind.Section)
        {
            return Fail($"There is no setting called \"{key}\".");
        }

        var set = await values.SetAsync(key, a["value"]?.DeepClone(), ct).ConfigureAwait(false);
        return set.IsOk ? $"Setting {key} changed." : Fail(set.Error!.Message);
    }

    private async Task<string> RememberAsync(string accountKey, string note, CancellationToken ct)
    {
        var config = settings.Config(accountKey);
        if (!config.Memory)
        {
            return Fail("Memory is turned off.");
        }

        var clean = note.Trim();
        if (clean.Length == 0)
        {
            return Fail("Nothing to remember.");
        }

        var saved = await settings.SaveAsync(accountKey, config with { Notes = [.. config.Notes, clean.Length > 500 ? clean[..500] : clean] }, ct).ConfigureAwait(false);
        return saved.IsOk ? "Noted." : Fail(saved.Error!.Message);
    }
}
