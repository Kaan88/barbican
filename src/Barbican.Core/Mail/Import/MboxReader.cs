// Mirrors: Rust mailimport.rs:175-230 Reader (reads the messages of one file one at a time; a multi-GB mbox never sits in memory whole; an .eml over the cap is not read at all);
// Bulwark lib/mail-import/engine.ts (streams mbox files in chunks). Stub from stage F of M2; owner unified.

using Barbican.Core.Common;

// CA1822: stub members become instance members with state when the owner implements them.
#pragma warning disable CA1822

namespace Barbican.Core.Mail.Import;

/// <summary>Streams the messages of an .eml or .mbox file.</summary>
public sealed class MboxReader
{
    /// <summary>An .eml larger than this is not read at all (no server takes a single message this big): 200 MB.</summary>
    public const long MaxEmlBytes = 200_000_000;

    /// <summary>Starts reading <paramref name="source"/>; <paramref name="emlCap"/> is the largest .eml accepted.</summary>
    public MboxReader(string fileName, Stream source, long emlCap)
    {
        throw new PortPendingException("mailimport.rs:188 Reader::new");
    }

    /// <summary>How many bytes of the file were read so far (progress).</summary>
    public long BytesRead => throw new PortPendingException("mailimport.rs:175 Reader.read");

    /// <summary>True when an .eml was over the cap and skipped.</summary>
    public bool TooBig => throw new PortPendingException("mailimport.rs:175 Reader.too_big");

    /// <summary>The next message, or null at the end (deleted messages are skipped).</summary>
    public ImportItem? NextItem()
    {
        throw new PortPendingException("mailimport.rs:223 Reader::next_item");
    }
}
