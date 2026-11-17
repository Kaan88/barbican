using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// A loopback HTTP server that counts requests: a remote image, stylesheet or font in a message
/// with remote content blocked must never reach it (the tracking-beacon check). Plain sockets,
/// so it needs no URL ACL on Windows.
/// </summary>
internal sealed class BeaconServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _hits = [];

    public BeaconServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyList<string> Hits
    {
        get
        {
            lock (_hits)
            {
                return [.. _hits];
            }
        }
    }

    public void Reset()
    {
        lock (_hits)
        {
            _hits.Clear();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (Exception)
            {
                return;
            }

            _ = ServeAsync(client, token);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buf = new byte[4096];
            var n = await stream.ReadAsync(buf, token);
            var line = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0].Trim();
            lock (_hits)
            {
                _hits.Add(line);
            }

            var body = "x"u8.ToArray();
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, token);
            await stream.WriteAsync(body, token);
        }
    }
}
