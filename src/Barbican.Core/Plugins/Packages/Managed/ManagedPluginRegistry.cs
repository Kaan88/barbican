// Mirrors Bulwark app/api/plugins/route.ts and lib/extension-package.ts. Parsing grants no trust.
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages.Inspection;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Bounded, inert metadata from a managed plugin registry.</summary>
public sealed partial class ManagedPluginRegistry
{
    private readonly ImmutableDictionary<string, ManagedPluginRegistryEntry> _entries;
    private ManagedPluginRegistry(ImmutableDictionary<string, ManagedPluginRegistryEntry> entries) => _entries = entries;
    public ManagedPluginRegistryEntry? FindEligible(string id) => _entries.GetValueOrDefault(id);

    public static Result<ManagedPluginRegistry> Parse(JsonElement root)
    {
        try
        {
            var budget = 100_000;
            if (!Unique(root, 0, ref budget) || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("plugins", out var rows) ||
                rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 512) throw new FormatException();
            var entries = ImmutableDictionary.CreateBuilder<string, ManagedPluginRegistryEntry>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var rawId) || rawId.ValueKind != JsonValueKind.String)
                    throw new FormatException();
                var id = rawId.GetString()!;
                if (id.Length > 256 || !ids.Add(id)) throw new FormatException();
                var entry = ParseRow(row, id);
                if (entry is not null) entries.Add(id, entry);
            }
            return Result<ManagedPluginRegistry>.Ok(new(entries.ToImmutable()));
        }
        catch (FormatException) { return Result<ManagedPluginRegistry>.Fail(new ValidationError("Invalid managed plugin registry.")); }
    }

    private static ManagedPluginRegistryEntry? ParseRow(JsonElement row, string id)
    {
        try
        {
            if (Flag(row, "dev", false) || Flag(row, "refused", false) || !Flag(row, "enabled", true)) return null;
            var name = Text(row, "name", 100, true);
            var version = Text(row, "version", 64, true);
            var author = Text(row, "author", 200, true);
            var description = Text(row, "description", 2000, false);
            var type = Text(row, "type", 32, true);
            var entry = Text(row, "entrypoint", 200, true);
            var hash = Text(row, "bundleHash", 64, true);
            var tier = OptionalText(row, "tier", 32) ?? "untrusted";
            if (!IdPattern().IsMatch(id) || !ManifestInspection.ValidVersion(version) || type is not ("ui-extension" or "sidebar-app" or "hook") ||
                !SafeEntry(entry) || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                tier is not ("untrusted" or "privileged")) return null;
            var permissions = Strings(row, "permissions", 256, 100, StringComparer.Ordinal, required: true);
            var privileged = string.Equals(tier, "privileged", StringComparison.Ordinal);
            if (permissions.Any(p => !ManifestPermissions.Known.Contains(p)) ||
                (privileged && (!row.TryGetProperty("dev", out var dev) || dev.ValueKind != JsonValueKind.False ||
                    !permissions.Contains("crypto:full", StringComparer.Ordinal))) ||
                (!privileged && permissions.Contains("keystore", StringComparer.Ordinal))) return null;
            var origins = Strings(row, "httpOrigins", 128, 200, StringComparer.OrdinalIgnoreCase);
            var paths = Strings(row, "apiPostPaths", 128, 200, StringComparer.Ordinal);
            if (origins.Any(origin => !OriginPattern().IsMatch(origin) || !Uri.TryCreate(origin.Replace("://*.", "://subdomain.", StringComparison.Ordinal), UriKind.Absolute, out var uri) || uri.Port <= 0) ||
                paths.Any(path => path.Contains("..", StringComparison.Ordinal) || !PathPattern().IsMatch(path))) return null;
            var credentials = Object(row, "credentials");
            var settings = Object(row, "settingsSchema");
            var locales = Object(row, "locales");
            // Credentials may authorize secret-bearing proxy requests. Until their complete
            // schema and host-origin policy are verified, never mark such a row eligible.
            if (credentials is { } credentialObject && credentialObject.EnumerateObject().Any()) return null;
            return new(id, name, version, author, description, type, entry, hash, tier, permissions, origins, paths,
                credentials, settings, locales, Flag(row, "forceEnabled", false), Flag(row, "defaultOn", true));
        }
        catch (FormatException) { return null; }
    }

    private static string Text(JsonElement row, string key, int max, bool required)
    {
        var value = OptionalText(row, key, max);
        if (required && string.IsNullOrWhiteSpace(value)) throw new FormatException();
        return value ?? "";
    }
    private static string? OptionalText(JsonElement row, string key, int max)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > max || text.Any(char.IsControl)) throw new FormatException();
        return text;
    }
    private static bool Flag(JsonElement row, string key, bool fallback) => !row.TryGetProperty(key, out var value) ? fallback : value.ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new FormatException() };
    private static JsonElement? Object(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException();
        return value.Clone();
    }
    private static ImmutableArray<string> Strings(JsonElement row, string key, int count, int max, StringComparer comparer, bool required = false)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        { if (required) throw new FormatException(); return []; }
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > count) throw new FormatException();
        var seen = new HashSet<string>(comparer);
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } text || text.Length > max || text.Any(char.IsControl) || !seen.Add(text)) throw new FormatException();
        return [.. seen];
    }
    private static bool SafeEntry(string entry) => !entry.StartsWith('/') && !entry.Contains('\\', StringComparison.Ordinal) &&
        entry.Split('/').All(part => part.Length > 0 && part is not ("." or "..") && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) &&
        (entry.EndsWith(".js", StringComparison.Ordinal) || entry.EndsWith(".mjs", StringComparison.Ordinal));
    private static bool Unique(JsonElement value, int depth, ref int budget)
    {
        if (depth > 32 || --budget < 0) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var property in value.EnumerateObject())
                if (++count > 4096 || !names.Add(property.Name) || !Unique(property.Value, depth + 1, ref budget)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 4096) return false;
            foreach (var item in value.EnumerateArray()) if (!Unique(item, depth + 1, ref budget)) return false;
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 100000) return false;
        return true;
    }
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex IdPattern();
    [GeneratedRegex(@"\Ahttps://(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)(?:\.(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?))*\.(?:[a-z](?:[a-z0-9-]*[a-z0-9])?)(?::[0-9]{1,5})?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex OriginPattern();
    [GeneratedRegex("^/api/[A-Za-z0-9._-]+[/A-Za-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex PathPattern();
}
