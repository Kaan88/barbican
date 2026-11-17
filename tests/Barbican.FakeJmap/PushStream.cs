// One open event stream of the fake server (RFC 8620 §7.3; WHATWG event stream format). Reads wait for the server to write, and
// honour cancellation, so the client's silence deadline (75 s) can be tested on a virtual clock. Owner jmap.

using System.Text;
using System.Threading.Channels;

namespace Barbican.FakeJmap;

/// <summary>A server-to-client byte stream fed through a channel.</summary>
public sealed class PushStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _current;

    /// <summary>True once the server or the client closed it.</summary>
    public bool IsClosed { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>Sends one event (<c>event:</c> name, <c>data:</c> lines, blank line).</summary>
    public void Send(string eventName, string data) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {data}\n\n"));

    /// <summary>Ends the stream (the client reads end of stream).</summary>
    public void End()
    {
        IsClosed = true;
        _chunks.Writer.TryComplete();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_current.IsEmpty)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            _chunks.Reader.TryRead(out var next);
            _current = next;
        }

        var n = Math.Min(buffer.Length, _current.Length);
        _current[..n].CopyTo(buffer);
        _current = _current[n..];
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async only");

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        End();
        base.Dispose(disposing);
    }
}
