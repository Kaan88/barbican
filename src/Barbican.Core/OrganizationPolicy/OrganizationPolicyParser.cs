// Bulwark lib/admin/organisation-policy.ts and policy-scopes.ts. Invalid relevant values fail closed.
using System.Text.Json;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.OrganizationPolicy;

public static class OrganizationPolicyParser
{
    private static readonly Regex Id = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
    private static readonly Regex ScopeId = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
    private static readonly Regex Domain = new("^(\\*\\.)?[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
    private static readonly Regex Group = new("^[^\\s@]+@[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
    private static readonly Regex Controls = new("[\\u0000-\\u001f\\u007f]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
    private static readonly string[] ReservedPrefixes = ["content-", "list-", "resent-", "arc-"];
    private static readonly HashSet<string> Reserved = new(["from", "to", "cc", "bcc", "subject", "date", "message-id", "sender", "reply-to", "in-reply-to", "references", "mime-version", "received", "return-path", "dkim-signature", "authentication-results", "sio-label", "sio-label-history", "sensitivity", "x-bulwark-label", "auto-submitted", "precedence", "importance", "x-priority", "disposition-notification-to"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses a full policy body. Callers must separately prove provider trust and full account coverage.</summary>
    public static Result<OrganizationPolicyDocument> Parse(JsonElement root)
    {
        try
        {
            Object(root);
            var global = Values(Required(root, "organisation")) with { Proofreading = ReadProofreading(root) };
            var scopesElement = Required(root, "scopes");
            if (scopesElement.ValueKind != JsonValueKind.Array || scopesElement.GetArrayLength() > 200) throw new FormatException();
            var scopes = new List<OrganizationPolicyScope>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scope in scopesElement.EnumerateArray())
            {
                Object(scope);
                var id = String(Required(scope, "id"), 64).Trim().ToLowerInvariant();
                if (!ScopeId.IsMatch(id) || !ids.Add(id)) throw new FormatException();
                var match = Required(scope, "match"); Object(match);
                if (match.EnumerateObject().Any(property => property.Name is not ("domains" or "groups"))) throw new FormatException();
                var domains = List(match, "domains", Domain);
                var groups = List(match, "groups", Group);
                var policy = Required(scope, "policy"); Object(policy);
                var values = policy.TryGetProperty("organisation", out var org) ? Values(org) : new OrganizationPolicyOverride(null, null, null);
                scopes.Add(new(id, domains, groups, values with { Proofreading = ReadProofreading(policy) }));
            }
            return Result<OrganizationPolicyDocument>.Ok(new(global, scopes));
        }
        catch (FormatException) { return Result<OrganizationPolicyDocument>.Fail(new ValidationError("The organisation policy is incomplete or malformed.")); }
    }

    private static ProofreadingPolicy ReadProofreading(JsonElement policy)
    {
        try { return ParseProofreading(policy); }
        catch (FormatException) { return ProofreadingPolicy.Empty with { IsValid = false }; }
    }

    private static ProofreadingPolicy ParseProofreading(JsonElement policy)
    {
        var defaults = ProofreadingPolicy.Empty.Defaults.ToBuilder();
        var restrictions = ProofreadingPolicy.Empty.Restrictions.ToBuilder();
        if (policy.TryGetProperty("defaults", out var values))
        {
            Object(values);
            foreach (var property in values.EnumerateObject().Where(property => IsProofreadingKey(property.Name)))
                defaults.Add(property.Name, Boolean(values, property.Name)!.Value);
        }
        if (policy.TryGetProperty("restrictions", out var rules))
        {
            Object(rules);
            foreach (var property in rules.EnumerateObject().Where(property => IsProofreadingKey(property.Name)))
            {
                Object(property.Value);
                var locked = Boolean(property.Value, "locked") ?? false;
                var value = Boolean(property.Value, "value");
                restrictions.Add(property.Name, new(locked, value));
            }
        }
        return new(defaults.ToImmutable(), restrictions.ToImmutable());
    }

    private static bool IsProofreadingKey(string key) =>
        string.Equals(key, "proofreading", StringComparison.Ordinal) || key.StartsWith("proofreading.", StringComparison.Ordinal);

    private static OrganizationPolicyOverride Values(JsonElement org)
    {
        Object(org);
        bool? noPrint = null, watermark = null;
        if (org.TryGetProperty("devices", out var devices))
        {
            Object(devices);
            noPrint = Boolean(devices, "noPrint"); watermark = Boolean(devices, "watermark");
        }
        ClassificationPolicy? labels = null;
        if (org.TryGetProperty("labels", out var raw)) labels = Labels(raw);
        return new(noPrint, watermark, labels);
    }

    private static ClassificationPolicy Labels(JsonElement raw)
    {
        Object(raw);
        var labels = new List<ClassificationLabel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (raw.TryGetProperty("list", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 50) throw new FormatException();
            foreach (var item in list.EnumerateArray())
            {
                Object(item);
                var id = String(Required(item, "id"), 64).Trim();
                var name = Line(Required(item, "name"), 100);
                if (!Id.IsMatch(id) || name.Length == 0 || !seen.Add(id)) throw new FormatException();
                var color = Optional(item, "color", 7, "#6b7280").Trim().ToLowerInvariant();
                if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ToString().All(char.IsAsciiHexDigit)) throw new FormatException();
                var sensitivity = Optional(item, "sensitivity", 30, "");
                if (sensitivity is not ("" or "Personal" or "Private" or "Company-Confidential")) throw new FormatException();
                labels.Add(new(id, name, color, Optional(item, "description", 1000, ""), OptionalLine(item, "marking", 200).Replace("\"", "", StringComparison.Ordinal).Replace("\\", "", StringComparison.Ordinal), sensitivity,
                    OptionalLine(item, "subjectPrefix", 60), OptionalLine(item, "extraHeaderValue", 500)));
            }
        }
        var defaultId = Optional(raw, "defaultId", 64, "").Trim();
        if (defaultId.Length > 0 && (!Id.IsMatch(defaultId) || !seen.Contains(defaultId))) throw new FormatException();
        var required = Boolean(raw, "required") ?? false;
        if (required && labels.Count == 0) throw new FormatException();
        var header = Optional(raw, "extraHeader", 76, "").Trim();
        if (!ValidHeader(header)) throw new FormatException();
        return new(labels, defaultId, required, header);
    }

    private static bool ValidHeader(string header) => header.Length == 0 || (header.All(c => c is >= '!' and <= '~' && c != ':') &&
        !Reserved.Contains(header) && !ReservedPrefixes.Any(prefix => header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    private static List<string> List(JsonElement obj, string key, Regex regex)
    {
        if (!obj.TryGetProperty(key, out var raw)) return [];
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 200) throw new FormatException();
        var list = new List<string>();
        foreach (var entry in raw.EnumerateArray())
        {
            var original = String(entry, 320).Trim().TrimEnd('.');
            string? value;
            if (ReferenceEquals(regex, Group))
                value = RecipientText.IsValidEmail(original) ? RecipientText.ToAsciiEmail(original).ToLowerInvariant() : null;
            else
            {
                var wildcard = original.StartsWith("*.", StringComparison.Ordinal);
                var domain = RecipientText.ToAsciiDomain(wildcard ? original[2..] : original);
                value = domain is null ? null : (wildcard ? "*." : "") + domain;
            }
            if (value is null || !regex.IsMatch(value)) throw new FormatException();
            if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
        }
        return list;
    }
    private static bool? Boolean(JsonElement obj, string key) => !obj.TryGetProperty(key, out var value) ? null : value.ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new FormatException(),
    };
    private static string OptionalLine(JsonElement obj, string key, int max) => obj.TryGetProperty(key, out var value) ? Line(value, max) : "";
    private static string Line(JsonElement value, int max) => Controls.Replace(String(value, max * 4), " ").Trim() is var text && text.Length > max ? text[..max] : text;
    private static string Optional(JsonElement obj, string key, int max, string fallback) => obj.TryGetProperty(key, out var value) ? String(value, max) : fallback;
    private static string String(JsonElement value, int max) => value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= max ? text : throw new FormatException();
    private static JsonElement Required(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) ? value : throw new FormatException();
    private static void Object(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw new FormatException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject()) if (!names.Add(property.Name)) throw new FormatException();
    }
}
