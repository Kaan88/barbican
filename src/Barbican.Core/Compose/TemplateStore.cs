// Mirrors: Rust templates.rs:12 list, :37 create, :52 delete (read, tombstone, write with ifInState, three tries), :78 tombstone; Bulwark lib/jmap/client.ts template metadata calls (org.bulwarkmail.template.<id> entries and the org.bulwarkmail.templates tombstone map),
// lib/jmap/bulwark-metadata.ts (the Inbox carries the metadata), stores/template-store.ts (EmailTemplate shape: id, name, subject, body, isHTML, category, isFavorite, createdAt, updatedAt); RFC 8620 section 5.3 (ifInState, stateMismatch).
// Stalwart crates/jmap/src/mailbox/set.rs (urn:ietf:params:jmap:metadata: a "metadata/<key>" patch on the mailbox; null removes the entry).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Compose;

/// <summary>Templates in the Inbox's JMAP metadata, shared with Bulwark.</summary>
public sealed class TemplateStore(IAccountManager accounts, TimeProvider time) : ITemplateStore
{
    private const string Metadata = "urn:ietf:params:jmap:metadata";
    private const string Namespace = "org.bulwarkmail";
    private const string Prefix = Namespace + ".template.";
    private const string TombstoneKey = Namespace + ".templates";
    private const int Tries = 3;

    public async Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct)
    {
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<IReadOnlyList<MailTemplate>>.Fail(ctx.Error!);
        }

        if (!ctx.Value!.Client.Session.Has(Metadata))
        {
            return Result<IReadOnlyList<MailTemplate>>.Ok([]);
        }

        var read = await ReadAsync(ctx.Value, ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<IReadOnlyList<MailTemplate>>.Fail(read.Error!);
        }

        var meta = read.Value.Metadata;
        var deleted = (meta[TombstoneKey] as JsonObject)?["deleted"] as JsonObject ?? [];
        IReadOnlyList<MailTemplate> list =
        [
            .. meta.Where(kv => kv.Key.StartsWith(Prefix, StringComparison.Ordinal) && !deleted.ContainsKey(kv.Key[Prefix.Length..]))
                .Select(kv => Read(kv.Key[Prefix.Length..], kv.Value))
                .OfType<MailTemplate>()
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
        ];
        return Result<IReadOnlyList<MailTemplate>>.Ok(list);
    }

    public async Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<MailTemplate>.Fail(ctx.Error!);
        }

        if (!ctx.Value!.Client.Session.Has(Metadata))
        {
            return Result<MailTemplate>.Fail(new ValidationError("This server cannot keep templates."));
        }

        var id = Guid.NewGuid().ToString();
        var now = time.GetUtcNow();
        var stamp = Stamp(now);
        var json = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["subject"] = subject,
            ["body"] = body,
            ["isHTML"] = isHtml,
            ["category"] = "",
            ["isFavorite"] = false,
            ["createdAt"] = stamp,
            ["updatedAt"] = stamp,
        };
        var inbox = await InboxAsync(ctx.Value, ct).ConfigureAwait(false);
        if (!inbox.IsOk)
        {
            return Result<MailTemplate>.Fail(inbox.Error!);
        }

        var write = await WriteAsync(ctx.Value, inbox.Value!, new JsonObject { ["metadata/" + Prefix + id] = json }, null, ct).ConfigureAwait(false);
        return write.IsOk
            ? Result<MailTemplate>.Ok(new MailTemplate(id, name, subject, body, isHtml) { CreatedAt = now, UpdatedAt = now })
            : Result<MailTemplate>.Fail(write.Error!);
    }

    public async Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateId);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        // read, change and write back the tombstone map whole, carrying the state it was read at: if another device changed the Inbox meanwhile the server answers stateMismatch and the delete starts over from a fresh read
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var read = await ReadAsync(ctx.Value!, ct).ConfigureAwait(false);
            if (!read.IsOk)
            {
                return Result<Unit>.Fail(read.Error!);
            }

            var (inbox, state, meta) = read.Value;
            var patch = new JsonObject
            {
                ["metadata/" + Prefix + templateId] = null,
                ["metadata/" + TombstoneKey] = TemplateTombstone.Mark(meta[TombstoneKey], templateId, time.GetUtcNow()),
            };
            var write = await WriteAsync(ctx.Value!, inbox, patch, state, ct).ConfigureAwait(false);
            if (write.IsOk)
            {
                return Result<Unit>.Ok(default);
            }

            if (write.Error is not MethodError { Type: "stateMismatch" })
            {
                return Result<Unit>.Fail(write.Error!);
            }
        }

        return Result<Unit>.Fail(new ValidationError("The templates kept changing on another device; try again."));
    }

    private static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static MailTemplate? Read(string id, JsonNode? v)
    {
        if (v is not JsonObject o)
        {
            return null;
        }

        return new MailTemplate(o.Str("id") ?? id, o.Str("name") ?? "", o.Str("subject") ?? "", o.Str("body") ?? "", o["isHTML"] is JsonValue h && h.TryGetValue<bool>(out var b) && b)
        {
            Category = o.Str("category") ?? "",
            IsFavorite = o["isFavorite"] is JsonValue f && f.TryGetValue<bool>(out var fav) && fav,
            CreatedAt = Time(o.Str("createdAt")),
            UpdatedAt = Time(o.Str("updatedAt")),
        };
    }

    private static DateTimeOffset Time(string? s) =>
        s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;

    private static async Task<Result<string>> InboxAsync(ComposeContext ctx, CancellationToken ct)
    {
        var roles = await ctx.RolesAsync(false, ct).ConfigureAwait(false);
        if (!roles.IsOk)
        {
            return Result<string>.Fail(roles.Error!);
        }

        return roles.Value!.Inbox is { } inbox ? Result<string>.Ok(inbox) : Result<string>.Fail(new ValidationError("No Inbox."));
    }

    private static async Task<Result<(string Inbox, string? State, JsonObject Metadata)>> ReadAsync(ComposeContext ctx, CancellationToken ct)
    {
        var inbox = await InboxAsync(ctx, ct).ConfigureAwait(false);
        if (!inbox.IsOk)
        {
            return Result<(string, string?, JsonObject)>.Fail(inbox.Error!);
        }

        var r = await ReaderCalls.CallAsync(ctx.Client, "Mailbox/get", new JsonObject { ["accountId"] = ctx.Account, ["ids"] = new JsonArray(inbox.Value), ["properties"] = new JsonArray("metadata") }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<(string, string?, JsonObject)>.Fail(r.Error!);
        }

        var meta = ((r.Value!["list"] as JsonArray)?.FirstOrDefault() as JsonObject)?["metadata"] as JsonObject;
        return Result<(string, string?, JsonObject)>.Ok((inbox.Value!, r.Value.Str("state"), meta is null ? [] : (JsonObject)meta.DeepClone()));
    }

    private static async Task<Result<Unit>> WriteAsync(ComposeContext ctx, string inbox, JsonObject patch, string? ifInState, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = ctx.Account, ["update"] = new JsonObject { [inbox] = patch } };
        if (ifInState is not null)
        {
            args["ifInState"] = ifInState;
        }

        var r = await ReaderCalls.CallAsync(ctx.Client, "Mailbox/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return r.Value!["notUpdated"] is JsonObject bad && bad[inbox] is { } err ? Result<Unit>.Fail(SetErrors.From(inbox, err)) : Result<Unit>.Ok(default);
    }
}
