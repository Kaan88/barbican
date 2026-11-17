// Mirrors: Rust jmap.rs:1108 watch (a read that stays silent for 75 s counts as a dead connection; ping=30 is Stalwart's minimum, so a
// live stream always speaks within the deadline), :1480 SseParser; RFC 8620 §7.3; WHATWG HTML §9.2 (event stream interpretation);
// Stalwart crates/jmap/src/api/event_source.rs (ping >= 30, `event: ping`). Owner jmap.

using System.Runtime.CompilerServices;
using System.Text;

namespace Barbican.Core.Jmap.Push;

/// <summary>Reads push events from an event stream, giving up when it stays silent past the deadline.</summary>
public static class EventSourceReader
{
    /// <summary>The silence after which a stream counts as dead.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(75);

    /// <summary>
    /// The events of <paramref name="stream"/> until it ends (the sequence completes) or stays silent for <paramref name="deadline"/>
    /// (a <see cref="TimeoutException"/>). The stream is disposed at the end.
    /// </summary>
    public static async IAsyncEnumerable<PushEvent> ReadAsync(Stream stream, TimeProvider time, TimeSpan deadline, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(time);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);
        var parser = new SseParser();
        while (true)
        {
            var line = await NextLineAsync(reader, time, deadline, ct).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (parser.Line(line) is { } ev)
            {
                yield return ev;
            }
        }
    }

    private static async Task<string?> NextLineAsync(StreamReader reader, TimeProvider time, TimeSpan deadline, CancellationToken ct)
    {
        using var silence = new CancellationTokenSource(deadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        try
        {
            return await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The push stream was silent for {deadline.TotalSeconds:0} s.", e);
        }
    }
}
