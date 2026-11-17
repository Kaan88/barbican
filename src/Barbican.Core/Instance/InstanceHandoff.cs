// Mirrors: Rust instance.rs:24 mailto_arg, :46 send, :74 read_request, :89 serve; PORT-PLAN.md section 3.9 (single instance, mailto handoff), plan D-241 (loopback TCP with a token, both OSes). Port by M3 composer-ui.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Mailto;

namespace Barbican.Core.Instance;

/// <summary>
/// The mailto handoff between launches over a loopback TCP socket (one mechanism on both OSes, D-241): a token line and a URL line, the server greets first, at most 8 connections at once, a short deadline per connection,
/// 256 KiB per request.
/// </summary>
public static class InstanceHandoff
{
    /// <summary>Connections handled at once; more are closed straight away.</summary>
    public const int MaxConnections = 8;

    /// <summary>The most bytes read from one connection.</summary>
    public const int MaxRequestBytes = 256 * 1024;

    /// <summary>The first command-line argument that is a mailto: link (as the OS passes it, quoted "%1").</summary>
    public static string? MailtoArg(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Select(a => a.Trim().Trim('"')).FirstOrDefault(a => MailtoLink.Parse(a) is not null);
    }

    /// <summary>One handoff from a connection: the URL when the token matches and the line is a mailto: link, else null (a foreign scheme such as file: is refused).</summary>
    public static string? ReadRequest(Stream stream, string token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(token);
        var buffer = new byte[MaxRequestBytes];
        var total = 0;
        try
        {
            var lines = 0;
            while (total < buffer.Length && lines < 2)
            {
                var n = stream.Read(buffer, total, buffer.Length - total);
                if (n <= 0)
                {
                    break;
                }

                for (var i = total; i < total + n; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        lines++;
                    }
                }

                total += n;
            }
        }
        catch (IOException)
        {
            return null;
        }

        var parts = Encoding.UTF8.GetString(buffer, 0, total).Split('\n');
        if (parts.Length < 2 || !string.Equals(parts[0].Trim(), token, StringComparison.Ordinal))
        {
            return null;
        }

        var url = parts[1].Trim();
        return MailtoLink.Parse(url) is not null ? url : null;
    }

    /// <summary>Hands <paramref name="url"/> to the instance at <paramref name="endpoint"/>: connects, checks the server's greeting, sends the token and the URL. False when the greeting is wrong (a foreign program on a stale port gets nothing) or the token is refused.</summary>
    public static bool Send(HandoffEndpoint endpoint, string url)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, endpoint.Port);

            var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;
            if (!string.Equals(ReadLine(stream), endpoint.Greet, StringComparison.Ordinal))
            {
                return false;
            }

            stream.ReadTimeout = 5000;
            var clean = url.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
            stream.Write(Encoding.UTF8.GetBytes(endpoint.Token + "\n" + clean + "\n"));
            return string.Equals(ReadLine(stream), "ok", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Serves handoffs from later launches until <paramref name="ct"/> is cancelled: each valid link goes to <paramref name="onLink"/>. Idle connections do not hold up a real handoff.</summary>
    public static async Task ServeAsync(TcpListener listener, string token, string greet, Action<string> onLink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(onLink);
        var busy = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (Volatile.Read(ref busy) >= MaxConnections)
                {
                    client.Dispose();
                    continue;
                }

                Interlocked.Increment(ref busy);
                _ = Task.Run(
                    () =>
                    {
                        try
                        {
                            Handle(client, token, greet, onLink);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref busy);
                        }
                    },
                    CancellationToken.None);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // the listener was stopped
        }
    }

    private static void Handle(TcpClient client, string token, string greet, Action<string> onLink)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 2000;
                stream.WriteTimeout = 2000;
                stream.Write(Encoding.UTF8.GetBytes(greet + "\n"));
                if (ReadRequest(stream, token) is { } url)
                {
                    onLink(url);
                    stream.Write("ok\n"u8);
                }
                else
                {
                    stream.Write("no\n"u8);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                // a dead or idle connection
            }
        }
    }

    private static string ReadLine(Stream stream)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (sb.Length < 4096 && stream.Read(one, 0, 1) == 1)
        {
            if (one[0] == (byte)'\n')
            {
                break;
            }

            sb.Append((char)one[0]);
        }

        return sb.ToString().Trim();
    }
}
