using System.Collections.Immutable;
using System.Security.Cryptography;
using FileAttributes = System.IO.FileAttributes;

namespace Barbican.App.Plugins.Sandbox;

// The root is supplied by the trusted host, not by plugin content. The manifest
// is just another pinned file and never supplies names or hashes to this verifier.
// Bulwark 1.21.2 source revision 933bc18f517135e38245821c5cc1bc7117ce2d64 with the audited host-origin isolation patch.
internal static class PluginSandboxBootstrapAssets
{
    internal const string EntryScript = "runtime-SCQLYMRU.js";
    internal const string Style = "kit-556dca3ab5af.css";

    private static readonly ImmutableArray<AssetPin> Pins =
    [
        new("manifest.json", 172, "259B3861DB585BF9C9AB7EE08DF761BE930212168D3BC969AD5E92108992C73D", "manifest"),
        new(EntryScript, 34836, "67BD5F7DB3B4C42FD233016387AECDF5CFC29F8F1544AC66A1FAEF78C6EACDA0", "entry script"),
        new(Style, 34515, "556DCA3AB5AF52E3882ED907D7C49FA3961E495AAD48DE67CFC3E33D2006184D", "stylesheet"),
        new("chunk-BWK7OKB3.js", 8534, "E3C6E952AF788637C143A8BDE4C23C178FB5A985D96FB69909FF3A9F0F1D15E4", "static preload"),
        new("chunk-DQG63ATX.js", 1593, "69409106994D2F4204CFEA8AD119B324B04FE62B1EC4E34ADB5077F16F34A92D", "static preload"),
        new("chunk-PIOFL43R.js", 297281, "CE546EEEF09A50217DEA942EDB397E3E8B87D7DF1B71B39DC136F0D876545F14", "slot preload"),
        new("chunk-CT56IUXM.js", 93637, "87B89A99C2DCC560F9D857787E60263C603C16386CDDA95F51389D0FA3AB7E63", "slot preload"),
        new("chunk-IQHV4AYX.js", 2486061, "BE5125664A99C94F2B64F5D5C312B7699F47FDA3E5AAE76320FC40FB6B2C572B", "dynamic icons"),
    ];

    internal static Task<IReadOnlyDictionary<string, PluginSandboxBootstrapAsset>> LoadAsync(
        string root, CancellationToken cancellationToken) => VerifyAsync(root, Pins, cancellationToken);

    internal sealed record AssetPin(string Name, int Size, string Sha256, string Purpose);

    // Custom pins permit small synthetic tests without weakening the production inventory.
    internal static async Task<IReadOnlyDictionary<string, PluginSandboxBootstrapAsset>> VerifyAsync(
        string root, ImmutableArray<AssetPin> pins, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (!Path.IsPathFullyQualified(root) || pins.IsDefaultOrEmpty)
            throw new InvalidDataException("A trusted absolute root and nonempty inventory are required.");
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            if (pin.Name.Length == 0 || pin.Name is "." or ".." || !string.Equals(Path.GetFileName(pin.Name), pin.Name, StringComparison.Ordinal) ||
                pin.Name.Contains('\\') || pin.Name.Contains('/') ||
                pin.Size < 0 || pin.Size > 3_000_000 || pin.Sha256.Length != 64 || !expected.Add(pin.Name))
                throw new InvalidDataException("Invalid asset inventory.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(root) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory)
            throw new InvalidDataException("Asset root must be an ordinary directory.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.Remove(Path.GetFileName(entry)) || !IsOrdinaryFile(entry))
                throw new InvalidDataException("Unexpected asset entry.");
        }
        if (expected.Count != 0) throw new InvalidDataException("Asset inventory is incomplete.");

        var result = ImmutableDictionary.CreateBuilder<string, PluginSandboxBootstrapAsset>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, pin.Name);
            // Length is pinned and the extra byte detects growth; no unbounded read.
            var content = new byte[pin.Size];
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            var extra = new byte[1];
            if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0 ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(content)), pin.Sha256, StringComparison.Ordinal) ||
                !IsOrdinaryFile(path))
                throw new InvalidDataException("Asset content does not match its pin.");
            result.Add(pin.Name, new PluginSandboxBootstrapAsset(pin.Purpose, ImmutableArray.CreateRange(content)));
        }
        return result.ToImmutable();
    }

    private static bool IsOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);
        return !attributes.HasFlag(FileAttributes.ReparsePoint) && !attributes.HasFlag(FileAttributes.Directory);
    }
}
