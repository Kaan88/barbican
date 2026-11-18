// Mirrors: G-012 (security review of untrusted vCard and photo input); Rust vcard.rs has no caps, the C# port adds them. Values are D-M5-03 defaults. Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>What the vCard reader accepts from an untrusted file.</summary>
public sealed record VCardLimits
{
    /// <summary>The whole file, in characters (over: the file is refused).</summary>
    public int MaxFileChars { get; init; } = 16 * 1024 * 1024;

    /// <summary>Cards read from one file (the rest are counted as skipped).</summary>
    public int MaxCards { get; init; } = 10_000;

    /// <summary>One unfolded line (a longer value is cut; a photo above <see cref="MaxPhotoChars"/> is dropped and the card stays).</summary>
    public int MaxLineChars { get; init; } = 1024 * 1024;

    /// <summary>The base64 text of one embedded photo (about 8 MiB of bytes).</summary>
    public int MaxPhotoChars { get; init; } = 11 * 1024 * 1024;

    /// <summary>Entries of one kind (emails, phones, ...) in one card.</summary>
    public int MaxEntriesPerKind { get; init; } = 500;
}
