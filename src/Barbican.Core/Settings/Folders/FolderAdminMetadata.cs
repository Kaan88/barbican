// Mirrors: Bulwark lib/jmap/bulwark-metadata.ts (the Inbox carries the private metadata; one entry per key under org.bulwarkmail.), lib/settings-metadata-sync.ts (a list or a map setting is the entry org.bulwarkmail.settings.value.<name-in-kebab-case> = { k, v, at }),
// Rust templates.rs:52 (read, change, write with ifInState, three tries); RFC 8620 section 5.3 (ifInState, stateMismatch); Stalwart crates/jmap/src/mailbox/set.rs (urn:ietf:params:jmap:metadata: a "metadata/<key>" patch on the mailbox; null removes the entry). D-M6-C03. Owner srv-data.
// The one writer of the Inbox metadata for the folder preferences and the vacation's calendar flag: read the Inbox metadata with the state, build the patch from it, write with ifInState, start again on stateMismatch.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Settings.Folders;

internal static class FolderAdminMetadata
{
    internal const string Capability = "urn:ietf:params:jmap:metadata";
    private const string ValuePrefix = "org.bulwarkmail.settings.value.";
    private const int Tries = 3;

    /// <summary>The metadata key of a list or map setting (<c>folderIcons</c> gives <c>org.bulwarkmail.settings.value.folder-icons</c>).</summary>
    public static string ValueKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ValuePrefix + string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i == 0 ? string.Empty : "-") + char.ToLowerInvariant(c) : c.ToString()));
    }

    /// <summary>The value <c>v</c> of a setting entry, or null.</summary>
    public static JsonNode? ValueOf(JsonObject metadata, string name) => (metadata[ValueKey(name)] as JsonObject)?["v"];

    /// <summary>The entry that stores <paramref name="value"/> for the setting <paramref name="name"/> as of <paramref name="now"/>.</summary>
    public static JsonObject Entry(string name, JsonNode value, DateTimeOffset now) => new()
    {
        ["k"] = name,
        ["v"] = value,
        ["at"] = now.ToUnixTimeMilliseconds(),
    };

    /// <summary>The Inbox metadata with the Mailbox state it was read at.</summary>
    public static async Task<Result<(string InboxId, string? State, JsonObject Metadata)>> ReadAsync(IJmapClient client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!client.Session.Has(Capability))
        {
            return Result<(string, string?, JsonObject)>.Fail(new ValidationError("This server cannot keep these settings."));
        }

        var boxes = await FilterAccess.MailboxesAsync(client, ct).ConfigureAwait(false);
        if (!boxes.IsOk)
        {
            return Result<(string, string?, JsonObject)>.Fail(boxes.Error!);
        }

        if (boxes.Value!.FirstOrDefault(b => string.Equals(b.Str("role"), "inbox", StringComparison.Ordinal)).Str("id") is not { } inbox)
        {
            return Result<(string, string?, JsonObject)>.Fail(new ValidationError("No Inbox."));
        }

        var args = new JsonObject { ["accountId"] = client.Session.MailAccountId, ["ids"] = new JsonArray(inbox), ["properties"] = new JsonArray("metadata") };
        var r = await ReaderCalls.CallAsync(client, "Mailbox/get", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<(string, string?, JsonObject)>.Fail(r.Error!);
        }

        var meta = ((r.Value!["list"] as JsonArray)?.FirstOrDefault() as JsonObject)?["metadata"] as JsonObject;
        return Result<(string, string?, JsonObject)>.Ok((inbox, r.Value.Str("state"), meta is null ? [] : (JsonObject)meta.DeepClone()));
    }

    /// <summary>Reads, lets <paramref name="build"/> make the patch from what is there (null: nothing to write), and writes it with ifInState; starts over from a fresh read when another device changed the Inbox meanwhile.</summary>
    public static async Task<Result<Unit>> UpdateAsync(IJmapClient client, Func<JsonObject, JsonObject?> build, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(build);
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var read = await ReadAsync(client, ct).ConfigureAwait(false);
            if (!read.IsOk)
            {
                return Result<Unit>.Fail(read.Error!);
            }

            var (inbox, state, meta) = read.Value;
            if (build(meta) is not { } patch)
            {
                return Result<Unit>.Ok(default);
            }

            var args = new JsonObject { ["accountId"] = client.Session.MailAccountId, ["update"] = new JsonObject { [inbox] = patch } };
            if (state is not null)
            {
                args["ifInState"] = state;
            }

            var w = await ReaderCalls.CallAsync(client, "Mailbox/set", args, ct).ConfigureAwait(false);
            if (w.IsOk)
            {
                return w.Value!["notUpdated"] is JsonObject bad && bad[inbox] is { } err ? Result<Unit>.Fail(SetErrors.From(inbox, err)) : Result<Unit>.Ok(default);
            }

            if (w.Error is not MethodError { Type: "stateMismatch" })
            {
                return Result<Unit>.Fail(w.Error!);
            }
        }

        return Result<Unit>.Fail(new ValidationError("These settings kept changing on another device; try again."));
    }

    /// <summary>A time stamp as the entries carry it.</summary>
    public static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
