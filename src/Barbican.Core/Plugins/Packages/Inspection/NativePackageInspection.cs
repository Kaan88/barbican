// Source: docs/milestones/M6b-contracts.md and NativePluginHost API 1; the native ZIP sidecar is host-defined.
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static class NativePackageInspection
{
    private static readonly Regex IdPattern = new(@"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex AssemblyPath = new(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,195}\.dll\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> Fields = new(["schemaVersion", "api", "id", "name", "version", "description", "mainAssembly", "hooks", "dependencies"], StringComparer.Ordinal);

    internal static Result<InspectedPluginPackage> Inspect(ImmutableDictionary<string, ImmutableArray<byte>> files, string archiveHash)
    {
        try
        {
            if (!files.TryGetValue("native-plugin.json", out var sidecar) || sidecar.Length > 512 * 1024) throw new InvalidDataException();
            var input = sidecar.AsMemory();
            if (input.Span.StartsWith(Encoding.UTF8.Preamble)) input = input[Encoding.UTF8.Preamble.Length..];
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 8 });
            var fields = ReadFields(document.RootElement);
            var id = Required(fields, "id", 64);
            if (!IdPattern.IsMatch(id)) throw new InvalidDataException();
            _ = Required(fields, "name", 100);
            var version = Required(fields, "version", 64);
            if (version.Any(char.IsControl) || !ManifestInspection.ValidVersion(version)) throw new InvalidDataException();
            _ = Text(fields, "description", 2000);
            var main = Required(fields, "mainAssembly", 200);
            if (!AssemblyPath.IsMatch(main)) throw new InvalidDataException();
            ValidateHooks(fields["hooks"]);
            var mainHash = ValidateDependencies(fields["dependencies"], files, main);
            return Result<InspectedPluginPackage>.Ok(new InspectedPluginPackage(id, version, main, document.RootElement.GetRawText(),
                [], [], archiveHash, mainHash, archiveHash, files)
            { RuntimeKind = PluginRuntimeKind.Native });
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or ArgumentException or InvalidOperationException)
        {
            return Result<InspectedPluginPackage>.Fail(new ValidationError("Invalid native plugin package."));
        }
    }

    private static Dictionary<string, JsonElement> ReadFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateObject())
            if (!Fields.Contains(item.Name) || !fields.TryAdd(item.Name, item.Value)) throw new InvalidDataException();
        if (fields.Count != Fields.Count || !Number(fields, "schemaVersion", 1) || !Number(fields, "api", NativePluginHost.ApiVersion))
            throw new InvalidDataException();
        return fields;
    }

    private static void ValidateHooks(JsonElement hooks)
    {
        if (hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() > 256) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hook in hooks.EnumerateArray())
        {
            if (hook.ValueKind != JsonValueKind.String || hook.GetString() is not { } value || !names.Add(value)
                || !(string.Equals(value, NativePluginHost.EmailDetailsHook, StringComparison.Ordinal) || HookPolicies.IsCatalogued(value)))
                throw new InvalidDataException();
        }
    }

    private static string ValidateDependencies(JsonElement dependencies, ImmutableDictionary<string, ImmutableArray<byte>> files, string main)
    {
        if (dependencies.ValueKind != JsonValueKind.Object || dependencies.GetPropertyCount() != files.Count - 1)
            throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? mainHash = null;
        foreach (var dependency in dependencies.EnumerateObject())
        {
            if (!AssemblyPath.IsMatch(dependency.Name) || !names.Add(dependency.Name) ||
                string.Equals(dependency.Name, "Barbican.Plugins.Abstractions.dll", StringComparison.OrdinalIgnoreCase) ||
                dependency.Value.ValueKind != JsonValueKind.String ||
                dependency.Value.GetString() is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigitLower) ||
                !files.TryGetValue(dependency.Name, out var bytes) || bytes.IsEmpty ||
                !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), digest, StringComparison.Ordinal))
                throw new InvalidDataException();
            // The package author supplied these bytes and hashes. Keep the DLL opaque until a trusted loading path can validate its PE and CLR metadata.
            if (string.Equals(dependency.Name, main, StringComparison.Ordinal)) mainHash = digest;
        }
        return mainHash ?? throw new InvalidDataException();
    }

    private static bool Number(Dictionary<string, JsonElement> fields, string key, int expected) => fields[key].ValueKind == JsonValueKind.Number && fields[key].TryGetInt32(out var value) && value == expected;
    private static string Text(Dictionary<string, JsonElement> fields, string key, int limit) => fields[key].ValueKind == JsonValueKind.String && fields[key].GetString() is { } value && value.Length <= limit ? value : throw new InvalidDataException();
    private static string Required(Dictionary<string, JsonElement> fields, string key, int limit) => Text(fields, key, limit) is { } value && !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException();
}
