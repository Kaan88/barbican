// Bulwark app/api/plugins/route.ts and lib/plugin-sandbox/{bundle-fetch,bundle-signing}.ts.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages.Inspection;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Builds the native package from an authenticated registry row and the exact code that the instance signed.</summary>
internal static class ManagedPluginArtifactFactory
{
    private const int MaxCodeBytes = 20 * 1024 * 1024;
    private const int MaxArchiveBytes = 5 * 1024 * 1024;
    private static readonly DateTimeOffset ArchiveTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static async Task<Result<ManagedPluginArtifact>> CreateAsync(ManagedPluginRegistry registry, string pluginId,
        ReadOnlyMemory<byte> receivedBytes, string pinnedPublicKey, string signature, string compatibilityVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ct.ThrowIfCancellationRequested();
        if (receivedBytes.Length is <= 0 or > MaxCodeBytes || registry.FindEligible(pluginId) is not { Tier: "privileged" } row ||
            !row.Permissions.Contains("crypto:full", StringComparer.Ordinal))
            return Failure("This managed bundle is unavailable.");
        var code = receivedBytes.ToArray();
        if (!ManagedBundleVerifier.Verify(code, pinnedPublicKey, row.BundleHash, signature))
            return Failure("The managed bundle does not match the registry and signing key.");

        using var archive = new MemoryStream();
        using (var output = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifestStream = Entry(output, "manifest.json"))
            using (var writer = new Utf8JsonWriter(manifestStream))
                WriteManifest(writer, row);
            using var script = Entry(output, row.Entrypoint);
            await script.WriteAsync(code, ct).ConfigureAwait(false);
        }
        if (archive.Length > MaxArchiveBytes) return Failure("The managed package exceeds the host limit.");
        archive.Position = 0;
        var inspected = await PluginPackageInspector.InspectAsync(archive, compatibilityVersion, ct).ConfigureAwait(false);
        if (!inspected.IsOk) return Result<ManagedPluginArtifact>.Fail(inspected.Error!);
        if (inspected.Value is not { } package || !Matches(row, package, code))
            return Failure("The managed package changed during validation.");
        var signer = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(pinnedPublicKey)));
        return Result<ManagedPluginArtifact>.Ok(new([.. archive.ToArray()], package, signer));
    }

    private static bool Matches(ManagedPluginRegistryEntry row, InspectedPluginPackage package, byte[] code)
    {
        if (package.RuntimeKind != PluginRuntimeKind.JavaScript || package.Resources.Count != 2 ||
            !string.Equals(package.BundleHash, row.BundleHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.Id, row.Id, StringComparison.Ordinal) ||
            !string.Equals(package.Version, row.Version, StringComparison.Ordinal) ||
            !string.Equals(package.EntryPoint, row.Entrypoint, StringComparison.Ordinal) ||
            !package.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(row.Permissions) ||
            !package.Resources.TryGetValue(package.EntryPoint, out var entry) || !entry.AsSpan().SequenceEqual(code))
            return false;
        using var manifest = JsonDocument.Parse(package.ManifestJson);
        var root = manifest.RootElement;
        return root.TryGetProperty("tier", out var tier) && string.Equals(tier.GetString(), "privileged", StringComparison.Ordinal) &&
            SameArray(root, "httpOrigins", row.HttpOrigins, StringComparer.OrdinalIgnoreCase) &&
            SameArray(root, "apiPostPaths", row.ApiPostPaths, StringComparer.Ordinal);
    }

    private static bool SameArray(JsonElement manifest, string key, IEnumerable<string> expected, StringComparer comparer)
    {
        var actual = manifest.TryGetProperty(key, out var values)
            ? values.EnumerateArray().Select(value => value.GetString()!) : Enumerable.Empty<string>();
        return actual.ToHashSet(comparer).SetEquals(expected);
    }

    private static Stream Entry(ZipArchive archive, string name)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = ArchiveTime;
        return entry.Open();
    }

    private static void WriteManifest(Utf8JsonWriter writer, ManagedPluginRegistryEntry row)
    {
        writer.WriteStartObject();
        writer.WriteString("id", row.Id);
        writer.WriteString("name", row.Name);
        writer.WriteString("version", row.Version);
        writer.WriteString("author", row.Author);
        writer.WriteString("description", row.Description);
        writer.WriteString("type", row.Type);
        writer.WriteString("entrypoint", row.Entrypoint);
        writer.WriteString("tier", "privileged");
        WriteStrings(writer, "permissions", row.Permissions);
        WriteStrings(writer, "httpOrigins", row.HttpOrigins);
        WriteStrings(writer, "apiPostPaths", row.ApiPostPaths);
        WriteValue(writer, "credentials", row.Credentials);
        WriteValue(writer, "settingsSchema", row.SettingsSchema);
        WriteValue(writer, "locales", row.Locales);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> entries)
    {
        writer.WriteStartArray(name);
        foreach (var entry in entries) writer.WriteStringValue(entry);
        writer.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, JsonElement? value)
    {
        if (value is not { } element) return;
        writer.WritePropertyName(name);
        element.WriteTo(writer);
    }

    private static Result<ManagedPluginArtifact> Failure(string message) => Result<ManagedPluginArtifact>.Fail(new ValidationError(message));
}
