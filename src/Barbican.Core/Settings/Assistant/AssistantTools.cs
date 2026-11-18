// Mirrors: see IAssistantTools; Rust agent.rs:48-150 (tool specs), :152-440 (exec), describe/label. B-AI-007, B-AI-008, B-AI-009. The tools call typed services (IMailService through the session, IMailWriter, IVacationService, IFilterService, ISettingValues), never raw JMAP; mail text goes back to the model inside <email> tags (B-AI-004).
// D-M6-A12: the tools this build runs are search_mail, read_message, read_thread, list_folders, move, archive, delete and mark messages, create_folder, get and set vacation, list_filters, change_setting and remember. draft_email, send_email, tag_messages, read_attachment, add and remove filter rule, the calendar and the
// contact tools need the composer, the tag service (Presentation) or services of other streams; they are not offered to the model until they exist, so it never calls a tool that cannot run.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Mail.Model;
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
    IAssistantSettings settings) : IAssistantTools
{
    private static readonly IReadOnlyList<AssistantToolSpec> All = BuildSpecs();

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
                "read_message" => await ReadMessageAsync(session, Str(args, "id"), ct).ConfigureAwait(false),
                "read_thread" => await ReadThreadAsync(session, Str(args, "id"), ct).ConfigureAwait(false),
                "list_folders" => await ListFoldersAsync(session, ct).ConfigureAwait(false),
                "move_messages" => await MoveAsync(session, args, null, ct).ConfigureAwait(false),
                "archive_messages" => await MoveAsync(session, args, "archive", ct).ConfigureAwait(false),
                "delete_messages" => await MoveAsync(session, args, "trash", ct).ConfigureAwait(false),
                "mark_messages" => await MarkAsync(session, args, ct).ConfigureAwait(false),
                "create_folder" => await CreateFolderAsync(session, args, ct).ConfigureAwait(false),
                "get_vacation" => await GetVacationAsync(accountKey, ct).ConfigureAwait(false),
                "set_vacation" => await SetVacationAsync(accountKey, args, ct).ConfigureAwait(false),
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
            new("read_message", "Read one email in full (headers, text, attachment names). Use id \"current\" for the message open in the reader.", Schema("""{"id": {"type": "string"}}""", "id"), false),
            new("read_thread", "Read the whole conversation an email belongs to (up to the last 20 messages). Use id \"current\" for the open message.", Schema("""{"id": {"type": "string"}}""", "id"), false),
            new("list_folders", "List mail folders with ids, roles and unread/total counts.", Schema("{}"), false),
            new("move_messages", "Move emails to a folder (by name or role).", Schema($$$"""{"ids": {{{ids}}}, "folder": {"type": "string"}}""", "ids", "folder"), true),
            new("archive_messages", "Move emails to Archive.", Schema($$$"""{"ids": {{{ids}}}}""", "ids"), true),
            new("delete_messages", "Move emails to Trash.", Schema($$$"""{"ids": {{{ids}}}}""", "ids"), true),
            new("mark_messages", "Mark emails read/unread/flagged/unflagged.", Schema($$$"""{"ids": {{{ids}}}, "mark": {"type": "string", "enum": ["read", "unread", "flagged", "unflagged"]}}""", "ids", "mark"), true),
            new("create_folder", "Create a mail folder, optionally inside a parent folder.", Schema("""{"name": {"type": "string"}, "parent": {"type": "string"}}""", "name"), true),
            new("get_vacation", "Read the out-of-office (vacation) auto-reply.", Schema("{}"), false),
            new("set_vacation", "Turn the out-of-office auto-reply on or off and set its text and dates.", Schema("""{"enabled": {"type": "boolean"}, "subject": {"type": "string"}, "body": {"type": "string"}, "from_date": {"type": "string", "description": "ISO date"}, "to_date": {"type": "string", "description": "ISO date"}}""", "enabled"), true),
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
