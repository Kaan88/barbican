// The Rust app's credential entries, in memory: UTF-16LE blobs as Rust keyring writes them on Windows. Read-only by construction (the
// interface has no write), and every blob handed out is kept so a test can check it was zeroed.

using System.Text;

namespace Barbican.Platform.Tests;

internal sealed class FakeLegacySource : ILegacyCredentialSource
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    public List<byte[]> HandedOut { get; } = [];

    public List<string> Reads { get; } = [];

    public FakeLegacySource Text(string target, string text) => Raw(target, Encoding.Unicode.GetBytes(text));

    public FakeLegacySource Raw(string target, byte[] blob)
    {
        _entries[target] = blob;
        return this;
    }

    public byte[]? Read(string target)
    {
        Reads.Add(target);
        if (!_entries.TryGetValue(target, out var blob))
        {
            return null;
        }

        var copy = (byte[])blob.Clone();
        HandedOut.Add(copy);
        return copy;
    }
}
