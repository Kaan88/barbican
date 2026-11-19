// Source: Bulwark lib/plugin-secrets/credentials.ts:92-112, 126-212; injected private-origin policy is a host-added input and never grants network access.
using System.Text.RegularExpressions;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static partial class ManifestInspection
{
    private static readonly Regex CredentialId = new("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> CredentialHeaders = new(["Authorization", "X-API-Key", "X-Goog-Api-Key", "DHL-API-Key", "17token", "secretkey"], StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, object?> Credentials(Dictionary<string, object?> input, Dictionary<string, object?>? config, IReadOnlySet<string> privateOrigins)
    {
        if (input.Count > 8) throw new InvalidDataException("Too many credentials.");
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (id, value) in input)
        {
            if (!CredentialId.IsMatch(id)) throw new InvalidDataException("Invalid credential id.");
            var item = AsObject(value);
            if (item.GetValueOrDefault("label") is not string label || string.IsNullOrWhiteSpace(label) || label.Length > 80)
                throw new InvalidDataException("Invalid credential label.");
            var injection = Injection(AsObject(item.GetValueOrDefault("inject")));
            string? origin = null;
            if (item.TryGetValue("origin", out var rawOrigin))
            {
                if (rawOrigin is not string text || text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                    || uri.UserInfo.Length > 0)
                    throw new InvalidDataException("Invalid credential origin.");
                origin = uri.GetLeftPart(UriPartial.Authority);
                if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && !(string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && privateOrigins.Contains(origin)))
                    throw new InvalidDataException("Invalid credential origin.");
            }
            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = label, ["inject"] = injection };
            if (item.GetValueOrDefault("scope") is not string scope || scope is not ("user" or "admin")) throw new InvalidDataException("Invalid credential scope.");
            normalized["scope"] = scope;
            if (origin is not null) normalized["origin"] = origin;
            if (string.Equals(scope, "admin", StringComparison.Ordinal))
            {
                var secret = ConfigKey(item, "secretKey", config, "secret");
                normalized["secretKey"] = secret;
                if (origin is null && !item.ContainsKey("originKey")) throw new InvalidDataException("Missing credential origin.");
                foreach (var key in new[] { "originKey", "usernameKey" })
                    if (item.ContainsKey(key)) normalized[key] = ConfigKey(item, key, config, null);
            }
            result[id] = normalized;
        }
        return result;
    }

    // Source: Bulwark lib/plugin-secrets/credentials.ts privateProxyOrigins. This is not a network grant.
    public static IReadOnlySet<string> SnapshotPrivateOrigins(IReadOnlyCollection<string> values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.UserInfo.Length > 0 || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("Invalid administrator origin policy.");
            result.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        return result;
    }

    private static string ConfigKey(Dictionary<string, object?> item, string key, Dictionary<string, object?>? config, string? requiredType)
    {
        if (item.GetValueOrDefault(key) is not string name || config is null || !config.TryGetValue(name, out var field)
            || (requiredType is not null && !string.Equals(AsObject(field).GetValueOrDefault("type") as string, requiredType, StringComparison.Ordinal)))
            throw new InvalidDataException("Invalid credential config reference.");
        return name;
    }

    private static Dictionary<string, object?> Injection(Dictionary<string, object?> input)
    {
        if (input.GetValueOrDefault("type") is not string type || type is not ("none" or "bearer" or "basic" or "header"))
            throw new InvalidDataException("Invalid credential injection.");
        var output = new Dictionary<string, object?>(StringComparer.Ordinal) { ["type"] = type };
        if (string.Equals(type, "header", StringComparison.Ordinal))
        {
            if (input.GetValueOrDefault("name") is not string name || !CredentialHeaders.Contains(name)) throw new InvalidDataException("Invalid credential header.");
            output["name"] = name;
            if (input.TryGetValue("prefix", out var value))
            {
                if (value is not string prefix || prefix.Length > 32 || prefix.Contains('\r') || prefix.Contains('\n')) throw new InvalidDataException("Invalid credential prefix.");
                output["prefix"] = prefix;
            }
        }
        return output;
    }
}
