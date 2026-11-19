// Source: Bulwark lib/extension-package.ts:223-398 and lib/extension-origins.ts:18-78 (schemas, locales, origins and paths).
using System.Text.RegularExpressions;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static partial class ManifestInspection
{
    private static readonly Regex FieldName = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex LocaleName = new("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Origin = new("^https://(?:\\*\\.)?(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)(?:\\.(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?))*\\.(?:[a-z](?:[a-z0-9-]*[a-z0-9])?)(?::[0-9]{1,5})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ApiPath = new("^/api/[A-Za-z0-9._-]+[/A-Za-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static void AddSchemas(Dictionary<string, object?> input, Dictionary<string, object?> output, List<string> warnings, IReadOnlySet<string> privateOrigins)
    {
        if (input.TryGetValue("settingsSchema", out var settings))
        {
            var result = SettingsSchema(AsObject(settings));
            if (result.Count > 0) output["settingsSchema"] = result;
        }
        Dictionary<string, object?>? config = null;
        if (input.TryGetValue("configSchema", out var configValue))
        {
            config = ConfigSchema(AsObject(configValue));
            if (config.Count > 0) output["configSchema"] = config;
        }
        if (input.TryGetValue("locales", out var locales))
        {
            var result = Locales(AsObject(locales));
            if (result.Count > 0) output["locales"] = result;
        }
        foreach (var name in new[] { "frameOrigins", "httpOrigins", "apiPostPaths" })
        {
            if (!input.TryGetValue(name, out var values)) continue;
            if (values is not List<object?> list) throw new InvalidDataException("Invalid origin list.");
            var kept = new List<object?>();
            var seen = new HashSet<string>(string.Equals(name, "apiPostPaths", StringComparison.Ordinal) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (var value in list)
            {
                if (value is not string text || !ValidOrigin(name, text)) { warnings.Add("Ignored an invalid origin or path."); continue; }
                if (seen.Add(text)) kept.Add(text);
                else if (!kept.Contains(text)) warnings.Add("Ignored an invalid origin or path.");
            }
            if (kept.Count > 0) output[name] = kept;
        }
        if (input.TryGetValue("credentials", out var credentials))
        {
            var valid = Credentials(AsObject(credentials), config, privateOrigins);
            if (valid.Count > 0) output["credentials"] = valid;
        }
    }

    private static Dictionary<string, object?> SettingsSchema(Dictionary<string, object?> input)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, raw) in input)
        {
            if (!FieldName.IsMatch(key)) throw new InvalidDataException("Invalid settings schema key.");
            var field = AsObject(raw);
            if (!field.TryGetValue("type", out var rawType) || rawType is not string type || type is not ("boolean" or "string" or "number" or "select")
                || !field.TryGetValue("label", out var rawLabel) || rawLabel is not string { Length: > 0 } label || !field.TryGetValue("default", out var def))
                throw new InvalidDataException("Invalid settings schema.");
            var item = new Dictionary<string, object?>(StringComparer.Ordinal) { ["type"] = type, ["label"] = label, ["default"] = def };
            if (field.TryGetValue("description", out var description)) item["description"] = description is string ? description : throw new InvalidDataException("Invalid schema description.");
            switch (type)
            {
                case "boolean" when def is not bool: throw new InvalidDataException("Invalid boolean default.");
                case "string" when def is not string: throw new InvalidDataException("Invalid string default.");
                case "number": ValidateNumberField(field, item, def); break;
                case "select":
                    if (!field.TryGetValue("options", out var rawOptions) || rawOptions is not List<object?> { Count: > 0 } options
                        || options.Any(o => o is not string) || def is not string || !options.Contains(def)) throw new InvalidDataException("Invalid select options.");
                    item["options"] = options; break;
            }
            result[key] = item;
        }
        return result;
    }

    private static void ValidateNumberField(Dictionary<string, object?> field, Dictionary<string, object?> item, object? value)
    {
        if (value is not double def || !double.IsFinite(def)) throw new InvalidDataException("Invalid numeric default.");
        foreach (var bound in new[] { "min", "max" })
        {
            if (!field.TryGetValue(bound, out var raw)) continue;
            if (raw is not double number || !double.IsFinite(number) || (string.Equals(bound, "min", StringComparison.Ordinal) ? def < number : def > number)) throw new InvalidDataException("Invalid numeric bound.");
            item[bound] = number;
        }
    }

    private static Dictionary<string, object?> ConfigSchema(Dictionary<string, object?> input)
    {
        foreach (var (key, raw) in input)
        {
            var field = AsObject(raw);
            if (!FieldName.IsMatch(key) || field.GetValueOrDefault("type") is not string type || type is not ("string" or "secret" or "boolean" or "number" or "select")
                || field.GetValueOrDefault("label") is not string) throw new InvalidDataException("Invalid configuration schema.");
            if (string.Equals(type, "select", StringComparison.Ordinal) && (field.GetValueOrDefault("options") is not List<object?> options
                || options.Any(o => o is not Dictionary<string, object?> option || option.GetValueOrDefault("value") is not string))) throw new InvalidDataException("Invalid configuration options.");
        }
        return input;
    }

    private static Dictionary<string, object?> Locales(Dictionary<string, object?> input)
    {
        if (input.Count > 64) throw new InvalidDataException("Too many locales.");
        foreach (var (locale, raw) in input)
        {
            var table = AsObject(raw);
            if (!LocaleName.IsMatch(locale) || table.Count > 2000 || table.Values.Any(v => v is not string)) throw new InvalidDataException("Invalid locale table.");
        }
        return input;
    }

    private static bool ValidOrigin(string name, string value) => value.Length <= 200 && (string.Equals(name, "apiPostPaths", StringComparison.Ordinal)
        ? !value.Contains("..", StringComparison.Ordinal) && ApiPath.IsMatch(value)
        : Origin.IsMatch(value));

    private static Dictionary<string, object?> AsObject(object? value) => value as Dictionary<string, object?> ?? throw new InvalidDataException("Expected an object.");
}
