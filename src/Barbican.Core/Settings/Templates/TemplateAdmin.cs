// Mirrors: Bulwark lib/template-utils.ts (exportTemplates :241: { version: 1, type: "webmail-templates", exportedAt, templates }; importTemplates :331: type and version checked, a template needs a name, new ids), stores/template-store.ts (create, update, delete and their hooks),
// lib/templates/metadata-templates.ts (the entry org.bulwarkmail.template.<id>), plugin-hook-catalog.ts:268-273; Rust templates.rs (the Inbox metadata with ifInState, three tries); RFC 8620 section 5.3; Stalwart crates/jmap/src/mailbox/set.rs (metadata). G-032..G-035, I-255, I-256. D-M6-C04. Owner srv-data.
// Imported HTML bodies are stored as they are and sanitised when they are inserted (the composer's DocHtml.Import), not here: the manager never renders them. Category and favourite flags of an import are dropped (the store creates plain templates).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Settings.Templates;

/// <summary>The default <see cref="ITemplateAdmin"/> over the composer's <see cref="ITemplateStore"/>: every call runs on the pool (D-M6-00).</summary>
public sealed class TemplateAdmin(IAccountManager accounts, ITemplateStore store, IHookBus hooks, TimeProvider time) : ITemplateAdmin
{
    private const string EntryPrefix = "org.bulwarkmail.template.";

    /// <inheritdoc />
    public event EventHandler<TemplateAdminChangedEventArgs>? Changed;

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountKey, CancellationToken ct) => Task.Run(() => store.ListAsync(accountKey, ct), ct);

    /// <inheritdoc />
    public Task<Result<MailTemplate>> CreateAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct) =>
        Task.Run(() => CreateCoreAsync(accountKey, name, subject, body, isHtml, ct), ct);

    /// <inheritdoc />
    public Task<Result<MailTemplate>> UpdateAsync(string accountKey, MailTemplate edited, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edited);
        return Task.Run(() => UpdateCoreAsync(accountKey, edited, ct), ct);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => Task.Run(() => DeleteCoreAsync(accountKey, id, ct), ct);

    /// <inheritdoc />
    public Task<Result<JsonObject>> ExportAsync(string accountKey, CancellationToken ct) => Task.Run(() => ExportCoreAsync(accountKey, ct), ct);

    /// <inheritdoc />
    public Task<Result<int>> ImportAsync(string accountKey, JsonNode document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Task.Run(() => ImportCoreAsync(accountKey, document, ct), ct);
    }

    private static TemplateAdminViewPayload View(MailTemplate t) => new(t.Id, t.Name, t.Subject, t.Body, t.IsHtml, t.Category, t.IsFavorite);

    private static string? NameProblem(string name) => TemplateText.ValidateName(name) switch
    {
        "empty" => "Give the template a name.",
        "too_long" => $"The template name is longer than {TemplateText.MaxNameLength} characters.",
        _ => null,
    };

    private static JsonObject Describe(MailTemplate t) => new()
    {
        ["id"] = t.Id,
        ["name"] = t.Name,
        ["subject"] = t.Subject,
        ["body"] = t.Body,
        ["isHTML"] = t.IsHtml,
        ["category"] = t.Category,
        ["isFavorite"] = t.IsFavorite,
        ["createdAt"] = FolderAdminMetadata.Iso(t.CreatedAt),
        ["updatedAt"] = FolderAdminMetadata.Iso(t.UpdatedAt),
    };

    // "Name", "Name (2)", "Name (3)": the first one no taken name (case-insensitive) is left
    private static string FreeName(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} ({n.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        }

        return candidate;
    }

    private async Task<Result<MailTemplate>> CreateCoreAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (NameProblem(name) is { } why)
        {
            return Result<MailTemplate>.Fail(new ValidationError(why));
        }

        var created = await store.CreateAsync(accountKey, name.Trim(), subject, body, isHtml, ct).ConfigureAwait(false);
        if (created.IsOk)
        {
            var t = created.Value!;
            hooks.Fire(HookNames.OnTemplateCreate, () => View(t));
            Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey));
        }

        return created;
    }

    private async Task<Result<MailTemplate>> UpdateCoreAsync(string accountKey, MailTemplate edited, CancellationToken ct)
    {
        if (NameProblem(edited.Name) is { } why)
        {
            return Result<MailTemplate>.Fail(new ValidationError(why));
        }

        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<MailTemplate>.Fail(client.Error!);
        }

        var key = EntryPrefix + edited.Id;
        var now = time.GetUtcNow();
        var missing = false;
        var done = await FolderAdminMetadata.UpdateAsync(client.Value!, meta =>
        {
            if (meta[key] is not JsonObject old)
            {
                missing = true;
                return null;
            }

            var entry = (JsonObject)old.DeepClone();
            entry["id"] = edited.Id;
            entry["name"] = edited.Name.Trim();
            entry["subject"] = edited.Subject;
            entry["body"] = edited.Body;
            entry["isHTML"] = edited.IsHtml;
            entry["category"] = edited.Category;
            entry["isFavorite"] = edited.IsFavorite;
            entry["updatedAt"] = FolderAdminMetadata.Iso(now);
            return new JsonObject { ["metadata/" + key] = entry };
        }, ct).ConfigureAwait(false);
        if (!done.IsOk)
        {
            return Result<MailTemplate>.Fail(done.Error!);
        }

        if (missing)
        {
            return Result<MailTemplate>.Fail(new ValidationError("The template no longer exists."));
        }

        var updated = edited with { Name = edited.Name.Trim(), UpdatedAt = now };
        hooks.Fire(HookNames.OnTemplateUpdate, () => View(updated));
        Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey));
        return Result<MailTemplate>.Ok(updated);
    }

    private async Task<Result<Unit>> DeleteCoreAsync(string accountKey, string id, CancellationToken ct)
    {
        var done = await store.DeleteAsync(accountKey, id, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            hooks.Fire(HookNames.OnTemplateDelete, () => new TemplateAdminDeletePayload(id));
            Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey));
        }

        return done;
    }

    private async Task<Result<JsonObject>> ExportCoreAsync(string accountKey, CancellationToken ct)
    {
        var list = await store.ListAsync(accountKey, ct).ConfigureAwait(false);
        if (!list.IsOk)
        {
            return Result<JsonObject>.Fail(list.Error!);
        }

        return Result<JsonObject>.Ok(new JsonObject
        {
            ["version"] = 1,
            ["type"] = "webmail-templates",
            ["exportedAt"] = FolderAdminMetadata.Iso(time.GetUtcNow()),
            ["templates"] = new JsonArray([.. list.Value!.Select(t => (JsonNode?)Describe(t))]),
        });
    }

    private async Task<Result<int>> ImportCoreAsync(string accountKey, JsonNode document, CancellationToken ct)
    {
        JsonArray? items = document switch
        {
            JsonArray a => a,
            JsonObject o when string.Equals(FilterRules.Str(o, "type"), "webmail-templates", StringComparison.Ordinal) && o["version"] is JsonValue v && v.TryGetValue<int>(out var n) && n == 1 => o["templates"] as JsonArray,
            _ => null,
        };
        if (items is null)
        {
            return Result<int>.Fail(new ValidationError("This is not a template export."));
        }

        var existing = await store.ListAsync(accountKey, ct).ConfigureAwait(false);
        if (!existing.IsOk)
        {
            return Result<int>.Fail(existing.Error!);
        }

        var taken = new HashSet<string>(existing.Value!.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var item in items.OfType<JsonObject>())
        {
            var name = FilterRules.Str(item, "name")?.Trim();
            if (string.IsNullOrEmpty(name) || NameProblem(name) is not null)
            {
                continue;
            }

            var unique = FreeName(name, taken);
            var made = await store.CreateAsync(accountKey, unique, FilterRules.Str(item, "subject") ?? string.Empty, FilterRules.Str(item, "body") ?? string.Empty, item["isHTML"] is JsonValue h && h.TryGetValue<bool>(out var html) && html, ct).ConfigureAwait(false);
            if (!made.IsOk)
            {
                return count > 0 ? Result<int>.FailPartial(made.Error!, count) : Result<int>.Fail(made.Error!);
            }

            taken.Add(unique);
            count++;
        }

        if (count > 0)
        {
            hooks.Fire(HookNames.OnTemplatesImport, () => new TemplateAdminImportPayload(count));
            Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey));
        }

        return Result<int>.Ok(count);
    }
}
