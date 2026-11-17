// Mirrors: M3 plan section 6.4 step 3 / section 9 item 17 (the autosave key: a stable 64-bit hash of the fields and the exported body; FNV-1a, stable within a process and across runs). Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>FNV-1a over strings: the content key of a composer.</summary>
public static class ComposerKey
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>The empty key to add parts to.</summary>
    public static ulong Start() => Offset;

    /// <summary>Mixes one part in (a separator keeps "ab"+"c" apart from "a"+"bc").</summary>
    public static ulong Add(ulong hash, string? part)
    {
        var h = hash;
        foreach (var c in part ?? string.Empty)
        {
            h = (h ^ c) * Prime;
            h = (h ^ (ulong)(c >> 8)) * Prime;
        }

        return (h ^ 0x1FUL) * Prime;
    }
}
