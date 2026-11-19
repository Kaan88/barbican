// Host-added Barbican native-plugin.json v1, not the Bulwark JS format; ZIP protections mirror extension-package.ts:54-96.
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
namespace Barbican.Core.Plugins.Packages.Inspection;

/// <summary>Inspects native package metadata and bytes without loading any assembly.</summary>
public static class NativePackageInspector
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*[a-z0-9]$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static async Task<Result<InspectedPluginPackage>> InspectAsync(Stream archive, CancellationToken ct)
    {
        try
        {
            var bytes = await PluginPackageInspector.ReadBoundedAsync(archive, 5 * 1024 * 1024, ct).ConfigureAwait(false);
            ZipStructure.Validate(bytes);
            using var input = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var files = await PluginPackageInspector.ReadEntriesAsync(zip, ct, native: true).ConfigureAwait(false);
            var root = PluginPackageInspector.RootOf(files.Keys);
            var resources = files.ToImmutableDictionary(x => x.Key[root.Length..], x => x.Value, StringComparer.Ordinal);
            if (!resources.TryGetValue("native-plugin.json", out var metadata) || metadata.Length > 512 * 1024 || resources.ContainsKey("manifest.json")) return Fail();
            using var document = JsonDocument.Parse(metadata.AsMemory(), new JsonDocumentOptions { MaxDepth = 32 });
            var manifest = document.RootElement;
            if (manifest.ValueKind != JsonValueKind.Object || DuplicateProperties(manifest)) return Fail();
            if (!manifest.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1
                || !manifest.TryGetProperty("api", out var api) || !api.TryGetInt32(out var apiVersion) || apiVersion != 1) return Fail();
            var id = Field(manifest, "id", 64);
            var name = Field(manifest, "name", 100);
            var version = Field(manifest, "version", 64);
            var description = Field(manifest, "description", 2000, allowEmpty: true);
            var entry = Field(manifest, "mainAssembly", 512);
            if (!IdPattern.IsMatch(id) || !ManifestInspection.ValidVersion(version) || !PluginPackageInspector.SafePath(entry) || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return Fail();
            if (!ValidContents(manifest, entry, resources)) return Fail();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var bundle = Convert.ToHexStringLower(SHA256.HashData(resources[entry].AsSpan()));
            var approval = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.GetRawText() + "\n" + hash)));
            _ = name; _ = description;
            return Result<InspectedPluginPackage>.Ok(new(id, version, entry, manifest.GetRawText(), [], [], hash, bundle, approval, resources, PluginRuntimeKind.Native));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or ArgumentException or FormatException or InvalidOperationException or OverflowException or NotSupportedException) { return Fail(); }
    }
    private static bool ValidContents(JsonElement manifest, string entry, ImmutableDictionary<string, ImmutableArray<byte>> resources)
    {
        if (!manifest.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() > 200) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hook in hooks.EnumerateArray())
            if (hook.ValueKind != JsonValueKind.String || hook.GetString() is not { Length: > 0 and <= 200 } text || !names.Add(text)) return false;
        if (!manifest.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object || !dependencies.TryGetProperty(entry, out _)) return false;
        var seen = 0;
        foreach (var dep in dependencies.EnumerateObject())
        {
            if (!PluginPackageInspector.SafePath(dep.Name) || string.Equals(dep.Name, "native-plugin.json", StringComparison.Ordinal) || dep.Value.ValueKind != JsonValueKind.String
                || !resources.TryGetValue(dep.Name, out var content) || !string.Equals(dep.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(content.AsSpan())), StringComparison.OrdinalIgnoreCase)) return false;
            seen++;
        }
        return seen == resources.Count - 1;
    }
    private static string Field(JsonElement manifest, string name, int max, bool allowEmpty = false)
    {
        if (!manifest.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > max || (!allowEmpty && string.IsNullOrWhiteSpace(text))) throw new InvalidDataException();
        return text;
    }
    private static bool DuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject()) if (!names.Add(prop.Name) || DuplicateProperties(prop.Value)) return true;
        return false;
    }
    private static Result<InspectedPluginPackage> Fail() => Result<InspectedPluginPackage>.Fail(new ValidationError("Invalid native plugin package."));
}
