// Mirrors: Bulwark lib/plugin-sandbox/host-api-account.ts:105-185 loadDirectory (the listing of one login's directory, kept five minutes; a failed or partial load is not kept for the next lookup).
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Account;

/// <summary>One login's directory as an add-on may see it: id, type, name and address of each principal.</summary>
internal sealed class PluginDirectoryCache(TimeProvider time)
{
    /// <summary>How long a listing serves later lookups (host-api-account.ts:109).</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _byLogin = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    internal sealed record Person(string Id, string Type, string Name, string Email);

    internal sealed record Listing(bool Complete, IReadOnlyList<Person> People);

    private sealed record Entry(DateTimeOffset At, Listing Listing);

    internal Listing? Get(string loginKey)
    {
        lock (_gate) return _byLogin.TryGetValue(loginKey, out var entry) && _time.GetUtcNow() - entry.At < Lifetime ? entry.Listing : null;
    }

    internal void Keep(string loginKey, Listing listing)
    {
        lock (_gate) _byLogin[loginKey] = new Entry(_time.GetUtcNow(), listing);
    }

    /// <summary>A principal as an add-on sees it: the display name (description, else name, else address, else id) and the lower-cased address (host-api-account.ts:158-168).</summary>
    internal static Person? Project(JsonNode principal)
    {
        if (principal is not JsonObject o || o["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) || id.Length == 0) return null;
        var email = Text(o, "email");
        var name = (Text(o, "description") is { Length: > 0 } d ? d : Text(o, "name") is { Length: > 0 } n ? n : email.Length > 0 ? email : id).Trim();
        var type = Text(o, "type") is { Length: > 0 } t ? t : "individual";
        return new Person(id, type, name, email.ToLowerInvariant());
    }

    private static string Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
