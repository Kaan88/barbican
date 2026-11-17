using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Automation.Protocol;

namespace Barbican.Automation;

/// <summary>
/// The line server (docs/adr/0007 decision 2; spec/automation-protocol.md): <c>127.0.0.1</c> only, on
/// the run's own port, one command per line in, one reply line out, the protocol of the Rust app's
/// automation.rs so tools/auto.py, motion.py and flicker.py work unchanged. Commands from all
/// connections run one at a time, in arrival order; a command that takes over 20 s replies <c>timeout</c>.
/// </summary>
internal sealed class AutomationServer : IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    private readonly AutomationContext _context;
    private readonly CommandRouter _router;
    private readonly int _port;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;

    public AutomationServer(AutomationContext context, CommandRouter router, int port)
    {
        _context = context;
        _router = router;
        _port = port;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _context.Window.Closed += (_, _) => Dispose();
        _ = Task.Run(() => AcceptLoopAsync(_listener, _stop.Token));
    }

    /// <summary>Stops accepting; the open connections end with the window.</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();
        _listener?.Dispose();
        _router.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            try
            {
                while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    var reply = line.Length > CommandLine.MaxLength ? "error line too long" : await RunOneAsync(line, cancellationToken).ConfigureAwait(false);
                    await writer.WriteLineAsync(CommandLine.OneLine(reply)).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<string> RunOneAsync(string line, CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var work = _router.DispatchAsync(line, cancellationToken);
            var done = await Task.WhenAny(work, Task.Delay(CommandTimeout, cancellationToken)).ConfigureAwait(false);
            if (done != work)
            {
                // The command may still open a popup later: the guard's timer keeps sweeping.
                return "timeout";
            }

            return await work.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // a failing command is a one-line error reply, never a dead server
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return "error " + e.GetType().Name + ": " + e.Message;
        }
        finally
        {
            _one.Release();
        }
    }
}
