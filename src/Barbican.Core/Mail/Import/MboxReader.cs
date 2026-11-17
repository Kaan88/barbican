// Mirrors: Rust mailimport.rs:174-229 Reader (reads the messages of one file one at a time; a multi-GB mbox never sits in memory whole; an .eml over the cap is
// not read at all), :14 MAX_EML; Bulwark lib/mail-import/engine.ts (streams mbox files in chunks), lib/eml-import.ts:17. Owner unified (M2 wave 2).

using System.Buffers;

namespace Barbican.Core.Mail.Import;

/// <summary>Streams the messages of an .eml or .mbox file. Not thread-safe; the import runs it on one background task.</summary>
public sealed class MboxReader
{
    /// <summary>An .eml larger than this is not read at all (no server takes a single message this big): 200 MB.</summary>
    public const long MaxEmlBytes = 200_000_000;

    private const int BufferSize = 64 * 1024;

    private readonly Stream _source;
    private readonly MboxSplitter? _mbox;
    private readonly long _emlCap;
    private readonly byte[] _buffer = new byte[BufferSize];
    private byte[]? _first;
    private int _start;
    private int _end;
    private bool _eof;
    private bool _done;

    /// <summary>Starts reading <paramref name="source"/>; <paramref name="emlCap"/> is the largest .eml accepted.</summary>
    public MboxReader(string fileName, Stream source, long emlCap)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _emlCap = emlCap;
        try
        {
            _first = ReadLine() ?? [];
        }
        catch (IOException)
        {
            _first = [];
            _done = true;
        }

        BytesRead = _first.Length;
        _mbox = !_done && MboxFormat.LooksMbox(fileName, _first) ? new MboxSplitter() : null;
    }

    /// <summary>How many bytes of the file were read so far (progress).</summary>
    public long BytesRead { get; private set; }

    /// <summary>True when an .eml was over the cap and skipped.</summary>
    public bool TooBig { get; private set; }

    /// <summary>The next message, or null at the end (deleted messages are skipped).</summary>
    public ImportItem? NextItem()
    {
        while (NextRaw() is { } raw)
        {
            if (MboxFormat.ToItem(raw) is { } item)
            {
                return item;
            }
        }

        return null;
    }

    private byte[]? NextRaw()
    {
        if (_done)
        {
            return null;
        }

        if (_mbox is { } splitter)
        {
            return NextOfMbox(splitter);
        }

        _done = true;
        return ReadEml();
    }

    private byte[]? NextOfMbox(MboxSplitter splitter)
    {
        if (_first is { } f)
        {
            _first = null;
            if (splitter.Push(f) is { } m)
            {
                return m;
            }
        }

        while (true)
        {
            byte[]? line;
            try
            {
                line = ReadLine();
            }
            catch (IOException)
            {
                line = null;
            }

            if (line is null)
            {
                _done = true;
                return splitter.Finish();
            }

            BytesRead += line.Length;
            if (splitter.Push(line) is { } m)
            {
                return m;
            }
        }
    }

    /// <summary>The whole file as one message, or null when it is blank or over the cap (read at most one byte past it: enough to know).</summary>
    private byte[]? ReadEml()
    {
        var first = _first ?? [];
        _first = null;
        var cap = Math.Max(0, _emlCap - first.Length);
        var message = new ArrayBufferWriter<byte>();
        message.Write(first);
        long n = 0;
        try
        {
            var limit = cap == long.MaxValue ? long.MaxValue : cap + 1;
            while (n < limit && Fill())
            {
                var take = (int)Math.Min(_end - _start, limit - n);
                message.Write(_buffer.AsSpan(_start, take));
                _start += take;
                n += take;
            }
        }
        catch (IOException)
        {
            return null;
        }

        BytesRead += n;
        if (n > cap)
        {
            TooBig = true;
            return null;
        }

        return MboxFormat.Blank(message.WrittenSpan) ? null : message.WrittenSpan.ToArray();
    }

    /// <summary>One line with its "\n", the rest of the file when it has none, or null at the end.</summary>
    private byte[]? ReadLine()
    {
        ArrayBufferWriter<byte>? partial = null;
        while (Fill())
        {
            var span = _buffer.AsSpan(_start, _end - _start);
            var nl = span.IndexOf((byte)'\n');
            if (nl >= 0)
            {
                var piece = span[..(nl + 1)];
                _start += nl + 1;
                if (partial is null)
                {
                    return piece.ToArray();
                }

                partial.Write(piece);
                return partial.WrittenSpan.ToArray();
            }

            partial ??= new ArrayBufferWriter<byte>();
            partial.Write(span);
            _start = _end;
        }

        return partial?.WrittenSpan.ToArray();
    }

    /// <summary>True when the buffer holds unread bytes, refilling it from the source when it is empty.</summary>
    private bool Fill()
    {
        if (_start < _end)
        {
            return true;
        }

        if (_eof)
        {
            return false;
        }

        _start = 0;
        _end = _source.Read(_buffer, 0, _buffer.Length);
        if (_end <= 0)
        {
            _end = 0;
            _eof = true;
            return false;
        }

        return true;
    }
}
