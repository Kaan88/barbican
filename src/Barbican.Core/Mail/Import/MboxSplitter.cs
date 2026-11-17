// Mirrors: Rust mailimport.rs:16-40 MboxSplitter (push one line at a time, a finished message comes back when the next separator shows up; finish); RFC 4155
// (mboxo and mboxrd: ">From " loses one ">"); Bulwark lib/mail-import/mbox.ts. Owner unified (M2 wave 2).

using System.Buffers;

namespace Barbican.Core.Mail.Import;

/// <summary>A streaming mbox splitter: a multi-GB mbox never sits in memory whole, only the message being read.</summary>
internal sealed class MboxSplitter
{
    private ArrayBufferWriter<byte>? _current;
    private byte[]? _pendingEmpty;

    /// <summary>One line with its line ending; returns a finished message when <paramref name="line"/> starts the next one.</summary>
    public byte[]? Push(ReadOnlySpan<byte> line)
    {
        var separator = line.StartsWith("From "u8) && (_current is null || _pendingEmpty is not null) && MboxFormat.IsEnvelopeLine(line);
        if (separator)
        {
            _pendingEmpty = null;
            var done = _current;
            _current = new ArrayBufferWriter<byte>();
            return Finished(done);
        }

        if (_current is null)
        {
            return null;
        }

        if (_pendingEmpty is { } e)
        {
            _current.Write(e);
            _pendingEmpty = null;
        }

        if (MboxFormat.IsEmptyLine(line))
        {
            _pendingEmpty = line.ToArray();
            return null;
        }

        var quotes = 0;
        while (quotes < line.Length && line[quotes] == (byte)'>')
        {
            quotes++;
        }

        _current.Write(quotes > 0 && line[quotes..].StartsWith("From "u8) ? line[1..] : line);
        return null;
    }

    /// <summary>The last message, at the end of the file.</summary>
    public byte[]? Finish()
    {
        _pendingEmpty = null;
        var done = _current;
        _current = null;
        return Finished(done);
    }

    private static byte[]? Finished(ArrayBufferWriter<byte>? done) =>
        done is null || MboxFormat.Blank(done.WrittenSpan) ? null : done.WrittenSpan.ToArray();
}
