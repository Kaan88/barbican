// Source: Bulwark lib/extension-package.ts:31-34, 54-96, 401-499, 517-569; aggregate limits and stricter ZIP checks are host-added restrictions.
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Plugins.Packages.Inspection;

/// <summary>Inspects plugin ZIPs without extracting files or executing code.</summary>
public static class PluginPackageInspector
{
    private const int ArchiveLimit = 5 * 1024 * 1024;
    private const int EntryLimit = 20 * 1024 * 1024;
    private const int TotalLimit = 40 * 1024 * 1024;
    private const int ManifestLimit = 512 * 1024;
    private const int EntriesLimit = 200;
    private const string JavaScriptManifest = "manifest.json";
    private const string NativeManifest = "native-plugin.json";
    private static readonly HashSet<string> Extensions = new([".js", ".mjs", ".css", ".json", ".png", ".svg", ".woff2", ".jpg", ".jpeg", ".webp"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The host compatibility version is a truthful Bulwark API version, separate from the product version.</summary>
    public static Task<Result<InspectedPluginPackage>> InspectAsync(Stream archive, string bulwarkCompatibilityVersion, CancellationToken ct) =>
        InspectAsync(archive, bulwarkCompatibilityVersion, [], ct);

    /// <summary>Admin-allowlisted proxy origins affect syntax acceptance only; the network broker still enforces live authority.</summary>
    public static async Task<Result<InspectedPluginPackage>> InspectAsync(Stream archive, string bulwarkCompatibilityVersion, IReadOnlyCollection<string> allowedPrivateOrigins, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(allowedPrivateOrigins);
        try
        {
            if (!ManifestInspection.ValidVersion(bulwarkCompatibilityVersion)) return Fail("Invalid host compatibility version.");
            var privateOrigins = ManifestInspection.SnapshotPrivateOrigins(allowedPrivateOrigins);
            var bytes = await ReadBoundedAsync(archive, ArchiveLimit, ct).ConfigureAwait(false);
            ZipStructure.Validate(bytes);
            using var input = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            if (zip.Entries.Count > EntriesLimit) return Fail("The archive has too many entries.");
            var root = RootOf(zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName));
            var native = zip.Entries.Any(e => string.Equals(e.FullName, root + NativeManifest, StringComparison.Ordinal));
            var files = await ReadEntriesAsync(zip, root, native, ct).ConfigureAwait(false);
            var normalized = files.ToImmutableDictionary(x => x.Key[root.Length..], x => x.Value, StringComparer.Ordinal);
            if (normalized.Keys.Contains(NativeManifest, StringComparer.OrdinalIgnoreCase))
            {
                if (!native || normalized.Keys.Contains(JavaScriptManifest, StringComparer.OrdinalIgnoreCase))
                    return Fail("A native package requires one unambiguous native-plugin.json manifest.");
                return NativePackageInspection.Inspect(normalized, Hash(bytes));
            }
            return InspectJavaScript(normalized, bulwarkCompatibilityVersion, privateOrigins, Hash(bytes));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException or FormatException or OverflowException or ArgumentException or NotSupportedException)
        {
            return Fail("The plugin package is invalid or exceeds its limits.");
        }
    }

    private static Result<InspectedPluginPackage> InspectJavaScript(ImmutableDictionary<string, ImmutableArray<byte>> normalized,
        string compatibilityVersion, IReadOnlySet<string> privateOrigins, string archiveHash)
    {
        if (!normalized.TryGetValue(JavaScriptManifest, out var manifestBytes) || manifestBytes.Length > ManifestLimit) return Fail("A bounded manifest.json is required.");
        var manifestInput = WithoutBom(manifestBytes);
        // Bulwark TextDecoder replaces malformed UTF-8, while JSON escapes retain lone UTF-16 code units.
        var parsed = JsJson.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifestInput))) as Dictionary<string, object?>;
        if (parsed is null) return Fail("The manifest must be an object.");
        var result = ManifestInspection.Validate(parsed, compatibilityVersion, privateOrigins);
        if (!result.IsOk) return Result<InspectedPluginPackage>.Fail(result.Error!);
        var manifest = result.Value!;
        if (!normalized.TryGetValue(manifest.EntryPoint, out var codeBytes)) return Fail("The declared entrypoint is missing.");
        var code = Encoding.UTF8.GetString(WithoutBom(codeBytes));
        AddCodeWarnings(code, manifest.Warnings);
        var canonical = JsJson.Canonical(manifest.Fields);
        var bundleHash = Hash(Encoding.UTF8.GetBytes(code));
        var approvalHash = Hash(Encoding.UTF8.GetBytes(canonical + "\n" + code));
        return Result<InspectedPluginPackage>.Ok(new InspectedPluginPackage(manifest.Id, manifest.Version, manifest.EntryPoint,
            canonical, manifest.Permissions.ToImmutableArray(), manifest.Warnings.ToImmutableArray(), archiveHash, bundleHash, approvalHash, normalized));
    }

    private static ReadOnlySpan<byte> WithoutBom(ImmutableArray<byte> bytes)
    {
        var input = bytes.AsSpan();
        return input.StartsWith(Encoding.UTF8.Preamble) ? input[Encoding.UTF8.Preamble.Length..] : input;
    }

    private static async Task<Dictionary<string, ImmutableArray<byte>>> ReadEntriesAsync(ZipArchive zip, string root, bool native, CancellationToken ct)
    {
        var paths = new PackagePaths();
        var result = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (!SafePath(name) || !paths.Add(name, name.EndsWith('/')) || IsLink(entry)) throw new InvalidDataException();
            if (name.EndsWith('/'))
            {
                await RequireEmptyDirectoryAsync(entry, ct).ConfigureAwait(false);
                continue;
            }
            var ext = Path.GetExtension(name);
            if (ext.Length > 0 && !Extensions.Contains(ext) && !(native && string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException();
            var manifestLimit = string.Equals(name, root + (native ? NativeManifest : JavaScriptManifest), StringComparison.Ordinal)
                ? ManifestLimit : EntryLimit;
            using var stream = await entry.OpenAsync(ct).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(stream, (int)Math.Min(TotalLimit - total, manifestLimit), ct).ConfigureAwait(false);
            if (Crc32.Compute(bytes) != entry.Crc32) throw new InvalidDataException();
            total += bytes.Length;
            if (total > TotalLimit) throw new InvalidDataException();
            result.Add(name, ImmutableArray.CreateRange(bytes));
        }
        return result;
    }

    private static async Task RequireEmptyDirectoryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        if (entry.Length != 0 || entry.Crc32 != 0) throw new InvalidDataException();
        using var content = await entry.OpenAsync(ct).ConfigureAwait(false);
        if (await content.ReadAsync(new byte[1], ct).ConfigureAwait(false) != 0) throw new InvalidDataException();
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        return mode is 0xA000 or 0x6000 or 0x2000 or 0x1000 or 0xC000 || (entry.ExternalAttributes & 0x400) != 0;
    }

    private static bool SafePath(string path)
    {
        if (path.Length == 0 || path.Length > 512 || !path.IsNormalized(NormalizationForm.FormC) || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.IndexOfAny([':', '<', '>', '"', '|', '?', '*']) >= 0) return false;
        var parts = path.TrimEnd('/').Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || part.Any(char.IsControl)) return false;
            var stem = part.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'))) return false;
        }
        return true;
    }

    private static string RootOf(IEnumerable<string> paths)
    {
        var all = paths.ToArray();
        if (all.Length == 0) return string.Empty;
        var slash = all[0].IndexOf('/', StringComparison.Ordinal);
        if (slash < 0) return string.Empty;
        var root = all[0][..(slash + 1)];
        return all.All(p => p.StartsWith(root, StringComparison.Ordinal)) ? root : string.Empty;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException();
            await output.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static void AddCodeWarnings(string code, List<string> warnings)
    {
        foreach (var pattern in new[] { @"\beval\s*\(", @"\bnew\s+Function\s*\(", @"document\.cookie", @"document\.write", @"innerHTML\s*=" })
            if (Regex.IsMatch(code, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)) warnings.Add("The source contains a pattern requiring review.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Result<InspectedPluginPackage> Fail(string message) => Result<InspectedPluginPackage>.Fail(new ValidationError(message));
}
