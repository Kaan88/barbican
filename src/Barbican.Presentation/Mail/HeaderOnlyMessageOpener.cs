// Mirrors: Rust msgload.rs prefill :175 (the header comes from the list row). Placeholder until the body stream's MessageLoader replaces it
// (M1-plan §4.3). Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>Shows the header of the row in the reader and nothing else (no body is fetched).</summary>
public sealed class HeaderOnlyMessageOpener(ReaderViewModel reader, Func<ReaderOptions> options) : IMessageOpener
{
    public void Open(MessageRowViewModel row, OpenReason reason, ListDirection direction)
    {
        ArgumentNullException.ThrowIfNull(row);
        reader.Apply(ReaderSnapshots.Prefill(row, options()));
    }

    public void Close() => reader.Clear();
}
