// Mirrors: Rust oauth.rs:166-:224 (bind 127.0.0.1:8791, else an ephemeral port; wait up to 5 minutes; other paths get 404; a small page
// for the browser tab); RFC 8252 §7.3 (loopback redirect), §8.3 (bind the loopback interface only); RFC 6749 §10.12 (state). spec
// B-OAUTH-008, B-OAUTH-010, B-OAUTH-011.
// Deliberate change (FABLE #3, M1-plan §6.3): Rust looked at `error` before `state`, so any local process could end a pending sign-in with
// "/callback?error=x". Here `state` is checked before anything else: a callback without the expected state is answered 400 and ignored
// (the wait goes on), and the first callback that carries it is the only one accepted; the listener then closes, so a second callback finds
// nothing listening.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Auth;

/// <summary>The one-shot loopback HTTP listener that receives the authorization code.</summary>
public sealed class LoopbackListener : IDisposable
{
    /// <summary>The fixed port tried first, so a pre-registered client can list an exact redirect URI (Rust oauth.rs:167).</summary>
    public const int PreferredPort = 8791;

    /// <summary>How long the browser has (Rust oauth.rs:188).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private const int MaxRequest = 8192;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private readonly TcpListener _listener;
    private int _refused;
    private int _closed;

    private LoopbackListener(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The port bound.</summary>
    public int Port { get; }

    /// <summary>The redirect URI to register and send.</summary>
    public Uri RedirectUri => new($"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}/callback");

    /// <summary>Callbacks refused because their <c>state</c> was missing or wrong.</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <summary>Binds 127.0.0.1 on <paramref name="preferredPort"/>, else on an ephemeral port (0 asks for an ephemeral port directly).</summary>
    public static Result<LoopbackListener> Start(int preferredPort = PreferredPort)
    {
        int[] ports = preferredPort == 0 ? [0] : [preferredPort, 0];
        foreach (var port in ports)
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Server.ExclusiveAddressUse = true;
            try
            {
                l.Start(backlog: 4);
                return Result<LoopbackListener>.Ok(new LoopbackListener(l));
            }
            catch (SocketException) when (port != 0)
            {
                l.Dispose();
            }
            catch (SocketException e)
            {
                l.Dispose();
                return Result<LoopbackListener>.Fail(new TransportError(e.Message, Retryable: false));
            }
        }

        return Result<LoopbackListener>.Fail(new TransportError("No loopback port.", Retryable: false));
    }

    /// <summary>
    /// Waits for the callback that carries <paramref name="expectedState"/> and returns its code. The provider's <c>error</c> on that callback
    /// fails the sign-in. Closes the listener when it returns.
    /// </summary>
    public async Task<Result<string>> WaitForCodeAsync(string expectedState, TimeSpan timeout, TimeProvider time, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedState);
        ArgumentNullException.ThrowIfNull(time);
        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            while (true)
            {
                TcpClient socket;
                try
                {
                    socket = await _listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Result<string>.Fail(new AuthError("Sign-in timed out.", NeedsSignIn: false));
                }

                using (socket)
                {
                    Result<string>? outcome;
                    try
                    {
                        outcome = await HandleAsync(socket, expectedState, time, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // the deadline passed while a connection was still being read or answered: the same timeout as an idle wait
                        return Result<string>.Fail(new AuthError("Sign-in timed out.", NeedsSignIn: false));
                    }

                    if (outcome is { } done)
                    {
                        return done;
                    }
                }
            }
        }
        finally
        {
            Close();
        }
    }

    public void Dispose() => Close();

    /// <summary>The query of a request target, decoded (application/x-www-form-urlencoded rules); the first of a repeated key wins.</summary>
    public static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var key = Decode(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? "" : Decode(part[(eq + 1)..]);
            q.TryAdd(key, value);
        }

        return q;
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    private static bool SameState(string? got, string expected) =>
        got is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(got), Encoding.UTF8.GetBytes(expected));

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, TimeProvider time, CancellationToken ct)
    {
        using var read = new CancellationTokenSource(ReadTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, read.Token);
        var buf = new byte[MaxRequest];
        var n = 0;
        try
        {
            while (n < buf.Length)
            {
                var got = await stream.ReadAsync(buf.AsMemory(n), linked.Token).ConfigureAwait(false);
                if (got == 0)
                {
                    break;
                }

                n += got;
                if (buf.AsSpan(0, n).IndexOf("\r\n"u8) >= 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(buf, 0, n);
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string reason, string? title, string? message, CancellationToken ct)
    {
        var page = title is null
            ? ""
            : $"<!doctype html><meta charset=utf-8><title>{title}</title><body style=\"font-family:Segoe UI,sans-serif;background:#111;color:#eee;display:grid;place-items:center;height:90vh\"><div><h2 style=\"color:#db2d54\">{title}</h2><p>{message}</p></div>";
        var body = Encoding.UTF8.GetBytes(page);
        var head = string.Create(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // the browser went away; the outcome stands
        }
    }

    // null: not the callback this sign-in waits for, keep listening
    private async Task<Result<string>?> HandleAsync(TcpClient socket, string expectedState, TimeProvider time, CancellationToken ct)
    {
        var stream = socket.GetStream();
        var line = await ReadRequestLineAsync(stream, time, ct).ConfigureAwait(false);
        var parts = line?.Split(' ');
        if (parts is not { Length: >= 2 } || !parts[1].StartsWith('/') || !Uri.TryCreate("http://127.0.0.1" + parts[1], UriKind.Absolute, out var target))
        {
            await RespondAsync(stream, 400, "Bad Request", null, null, ct).ConfigureAwait(false);
            return null;
        }

        if (!string.Equals(target.AbsolutePath, "/callback", StringComparison.Ordinal))
        {
            await RespondAsync(stream, 404, "Not Found", null, null, ct).ConfigureAwait(false);
            return null;
        }

        var q = ParseQuery(target.Query);

        // state first (FABLE #3): nothing else in a callback counts until it proves it answers this request
        if (!SameState(q.GetValueOrDefault("state"), expectedState))
        {
            Interlocked.Increment(ref _refused);
            await RespondAsync(stream, 400, "Bad Request", "Sign-in failed", "This answer does not belong to the sign-in Barbican is waiting for.", ct).ConfigureAwait(false);
            return null;
        }

        if (q.TryGetValue("error", out var error))
        {
            await RespondAsync(stream, 200, "OK", "Sign-in failed", "Barbican could not sign you in. Return to the app for details.", ct).ConfigureAwait(false);
            var description = q.GetValueOrDefault("error_description") ?? "";
            return Result<string>.Fail(new AuthError($"The provider refused the sign-in: {error} {description}".TrimEnd(), NeedsSignIn: false));
        }

        if (q.GetValueOrDefault("code") is not { Length: > 0 } code)
        {
            await RespondAsync(stream, 400, "Bad Request", "Sign-in failed", "The answer carried no authorization code.", ct).ConfigureAwait(false);
            return null;
        }

        await RespondAsync(stream, 200, "OK", "Signed in", "You are signed in to Barbican. You can close this tab.", ct).ConfigureAwait(false);
        return Result<string>.Ok(code);
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _listener.Stop();
            _listener.Dispose();
        }
    }
}
