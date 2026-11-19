// Bulwark lib/extension-package.ts:176-220, 401-499.
using System.Globalization;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static partial class ManifestInspection
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*[a-z0-9]$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex VersionPattern = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool ValidVersion(string? version) => version is { Length: > 0 and <= 64 } && VersionPattern.IsMatch(version);

    public static Result<ManifestDefinition> Validate(Dictionary<string, object?> input, string host, IReadOnlySet<string> privateOrigins)
    {
        try
        {
            var warnings = new List<string>();
            var id = Required(input, "id", 64);
            if (!IdPattern.IsMatch(id)) throw new InvalidDataException("Invalid plugin id.");
            var name = Required(input, "name", 100);
            var version = Required(input, "version", 64);
            if (!ValidVersion(version)) throw new InvalidDataException("Invalid plugin version.");
            var author = Required(input, "author", 200);
            var description = Optional(input, "description", 2000) ?? string.Empty;
            var type = Required(input, "type", 200);
            if (type is not ("ui-extension" or "sidebar-app" or "hook")) throw new InvalidDataException("Invalid plugin type.");
            var entry = input.TryGetValue("entrypoint", out var e) && e is string { Length: > 0 } path ? path : "index.js";
            if (!SafeEntry(entry)) throw new InvalidDataException("Invalid entrypoint.");
            var tier = Optional(input, "tier", 200);
            if (tier is not (null or "untrusted" or "privileged")) throw new InvalidDataException("Invalid tier.");
            var permissions = Permissions(input, tier, warnings);
            var minimum = Optional(input, "minAppVersion", 32);
            if (minimum is not null && (!ValidVersion(minimum) || CompareVersions(host, minimum) < 0)) throw new InvalidDataException("Incompatible plugin version.");
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = id, ["name"] = name, ["version"] = version, ["author"] = author, ["description"] = description,
                ["type"] = type, ["permissions"] = permissions.Cast<object?>().ToList(), ["entrypoint"] = entry,
            };
            if (tier is not null) fields["tier"] = tier;
            if (minimum is not null) fields["minAppVersion"] = minimum;
            AddSchemas(input, fields, warnings, privateOrigins);
            return Result<ManifestDefinition>.Ok(new ManifestDefinition(id, version, entry, fields, permissions, warnings));
        }
        catch (InvalidDataException e) { return Result<ManifestDefinition>.Fail(new ValidationError(e.Message)); }
    }

    private static string Required(Dictionary<string, object?> input, string key, int limit)
    {
        var value = Optional(input, key, limit);
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("A required manifest field is missing.");
    }

    private static string? Optional(Dictionary<string, object?> input, string key, int limit)
    {
        if (!input.TryGetValue(key, out var value)) return null;
        return value is string text && text.Length <= limit ? text : throw new InvalidDataException("Invalid manifest field.");
    }

    private static bool SafeEntry(string entry) => entry.Length <= 200 && !entry.StartsWith('/') && !entry.Contains('\\', StringComparison.Ordinal)
        && entry.Split('/').All(x => x.Length > 0 && x is not ("." or ".."))
        && (entry.EndsWith(".js", StringComparison.Ordinal) || entry.EndsWith(".mjs", StringComparison.Ordinal));

    private static int CompareVersions(string a, string b)
    {
        var left = a.Split('-', '+')[0].Split('.');
        var right = b.Split('-', '+')[0].Split('.');
        for (var i = 0; i < 3; i++)
        {
            var comparison = double.Parse(left[i], CultureInfo.InvariantCulture).CompareTo(double.Parse(right[i], CultureInfo.InvariantCulture));
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static List<string> Permissions(Dictionary<string, object?> input, string? tier, List<string> warnings)
    {
        var result = new List<string>();
        foreach (var key in new[] { "permissions", "optionalPermissions" })
        {
            if (!input.TryGetValue(key, out var value)) continue;
            if (value is not List<object?> list || list.Any(x => x is not string)) throw new InvalidDataException("Invalid permissions.");
            foreach (var perm in list.Cast<string>())
            {
                if (string.Equals(perm, "smime:read", StringComparison.Ordinal)) { if (string.Equals(key, "permissions", StringComparison.Ordinal)) warnings.Add("Ignored a deprecated permission."); continue; }
                if (!ManifestPermissions.Known.Contains(perm))
                {
                    if (string.Equals(key, "permissions", StringComparison.Ordinal)) throw new InvalidDataException("Unknown required permission.");
                    warnings.Add("Ignored an unknown optional permission.");
                    continue;
                }
                if (!result.Contains(perm, StringComparer.Ordinal)) result.Add(perm);
            }
        }
        if (!string.Equals(tier, "privileged", StringComparison.Ordinal) && result.Remove("keystore")) warnings.Add("The keystore permission needs privileged tier.");
        if (string.Equals(tier, "privileged", StringComparison.Ordinal) && !result.Contains("crypto:full", StringComparer.Ordinal)) throw new InvalidDataException("Privileged tier requires crypto:full.");
        return result;
    }
}
