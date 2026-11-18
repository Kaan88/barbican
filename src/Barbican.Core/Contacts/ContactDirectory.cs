// Mirrors: Rust contacts_x.rs:847 PRINCIPALS, :848 PRINCIPAL_PROPS, :858 fetch_directory (Principal/query with Principal/get back-referenced, paged; the Principal/get without ids when the server refuses the query), :850 directory_enabled, :893 load_directory (resources without an email left out, sorted by name);
// Bulwark components/contacts/directory-browser.tsx; RFC 9670 (JMAP Principals); Stalwart crates/jmap/src/principal/. B-CON-025; setting showContactsDirectory (D-M5-13). Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Storage;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactDirectory"/>.</summary>
public sealed class ContactDirectory(IAccountManager accounts, IPrefsStore? prefs = null) : IContactDirectory
{
    /// <summary>The seven properties asked of every principal (Rust PRINCIPAL_PROPS).</summary>
    public static readonly IReadOnlyList<string> PrincipalProps = ["id", "type", "name", "description", "email", "timeZone", "accounts"];

    private const int PageCap = 500;
    private const int Ceiling = 20_000;

    /// <summary>The principals the directory lists: resources without an email address are left out (they cannot be written to), sorted by name without regard to case.</summary>
    public static IReadOnlyList<JsonNode> Listed(IReadOnlyList<JsonNode> principals)
    {
        ArgumentNullException.ThrowIfNull(principals);
        return [.. principals.Where(p => !string.Equals(p.Str("type"), "resource", StringComparison.Ordinal) || p.Str("email") is not null).OrderBy(p => (p.Str("name") ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal)];
    }

    public bool IsAvailable(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        return accounts.SessionFor(accountKey)?.Client.Session.Has(ContactOptions.PrincipalsCapability) == true && (prefs is null || prefs.Current.Bool("showContactsDirectory", true));
    }

    public async Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        if (!IsAvailable(accountKey) || accounts.SessionFor(accountKey)?.Client is not { } client)
        {
            return Result<(IReadOnlyList<JsonNode>, bool)>.Fail(new ValidationError("This server has no directory."));
        }

        var account = client.Session.AccountFor(ContactOptions.PrincipalsCapability);
        var props = new JsonArray([.. PrincipalProps.Select(p => (JsonNode)p)]);
        var all = new List<JsonNode>();
        var page = Math.Min(client.Session.Limits.MaxObjectsInGet, PageCap);
        var pos = 0;
        while (pos <= Ceiling)
        {
            var request = JmapRequest.Of(
                JmapRequest.Call("Principal/query", new JsonObject { ["accountId"] = account, ["position"] = pos, ["limit"] = page }, "0"),
                JmapRequest.Call("Principal/get", new JsonObject { ["accountId"] = account, ["#ids"] = new JsonObject { ["resultOf"] = "0", ["name"] = "Principal/query", ["path"] = "/ids" }, ["properties"] = props.DeepClone() }, "1"));
            var r = await client.CallAsync(request, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                // a server that refuses the query (directory queries off) still answers a get without ids with the principals the login already knows
                return r.Error is MethodError { Type: "forbidden" } ? await FallbackAsync(client, account, props, ct).ConfigureAwait(false) : Result<(IReadOnlyList<JsonNode>, bool)>.Fail(r.Error!);
            }

            var ids = r.Value!.ByCallId("0") is { Count: > 1 } q ? q[1].Strings("ids").Count : 0;
            all.AddRange(r.Value.ByCallId("1") is { Count: > 1 } g ? (g[1].Arr("list") ?? []).OfType<JsonObject>().Select(o => (JsonNode)o.DeepClone()) : []);
            if (ids < page)
            {
                break;
            }

            pos += ids;
        }

        return Result<(IReadOnlyList<JsonNode>, bool)>.Ok((all, false));
    }

    private static async Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> FallbackAsync(IJmapClient client, string account, JsonArray props, CancellationToken ct)
    {
        var got = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Principal/get", new JsonObject { ["accountId"] = account, ["ids"] = null, ["properties"] = props.DeepClone() }, "0")), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<(IReadOnlyList<JsonNode>, bool)>.Fail(got.Error!);
        }

        var list = (got.Value!.ByCallId("0") is { Count: > 1 } g ? g[1].Arr("list") : null) ?? [];
        return Result<(IReadOnlyList<JsonNode>, bool)>.Ok(([.. list.OfType<JsonObject>().Select(o => (JsonNode)o.DeepClone())], true));
    }
}
