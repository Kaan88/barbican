// Mirrors: Rust instance.rs:12 file, :35 read_endpoint, :64 random_hex, :124 listen, :145 cleanup; plan D-241. Added by M3 composer-ui (additive).

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Barbican.Core.Instance;

/// <summary>
/// One running instance's side of the mailto handoff: listens on a free loopback port, records the port, a 128-bit token and a greeting in <c>instance.json</c> of the profile's config folder (user-only on Linux), and hands links
/// from a later launch to the callback. A later launch calls <see cref="Forward"/> first. Test and automation instances pass their own <c>suffix</c> so they never take links meant for the user's own Barbican.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _file;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private HandoffEndpoint? _mine;

    public SingleInstance(string configDir, string? suffix = null)
    {
        ArgumentNullException.ThrowIfNull(configDir);
        _file = Path.Combine(configDir, string.IsNullOrWhiteSpace(suffix) ? "instance.json" : $"instance-{suffix.Trim()}.json");
    }

    /// <summary>Sends the link to a running instance; true when one took it (this process can exit).</summary>
    public bool Forward(string url) => ReadEndpoint() is { } e && InstanceHandoff.Send(e, url);

    /// <summary>Starts listening and records where. False when there is no randomness (a guessable token would let any local program inject links), no port, or the file cannot be written: then this instance does not listen.</summary>
    public bool Listen(Action<string> onLink)
    {
        ArgumentNullException.ThrowIfNull(onLink);
        string token;
        string greet;
        try
        {
            token = RandomHex();
            greet = "barbican " + RandomHex();
        }
        catch (CryptographicException)
        {
            return false;
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var pid = Environment.ProcessId;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var json = new JsonObject { ["port"] = port, ["token"] = token, ["greet"] = greet, ["pid"] = pid }.ToJsonString();
            File.WriteAllText(_file, json);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _mine = new HandoffEndpoint(port, token, greet, pid);
        }
        catch (Exception e) when (e is IOException or SocketException or UnauthorizedAccessException)
        {
            listener.Stop();
            return false;
        }

        _listener = listener;
        _ = InstanceHandoff.ServeAsync(listener, token, greet, onLink, _cts.Token);
        return true;
    }

    /// <summary>Clean exit: stops listening and removes the file when it still points at this instance (a later instance may have taken over).</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        if (_mine is { } mine && ReadEndpoint() == mine)
        {
            try
            {
                File.Delete(_file);
            }
            catch (IOException)
            {
                // left behind: the next launch finds a dead port and starts on its own
            }
        }

        _cts.Dispose();
    }

    private static string RandomHex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower(CultureInfo.InvariantCulture);

    private HandoffEndpoint? ReadEndpoint()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_file)) is not JsonObject o || o["port"] is null || o["token"] is null || o["greet"] is null)
            {
                return null;
            }

            return new HandoffEndpoint((int)o["port"]!, (string)o["token"]!, (string)o["greet"]!, o["pid"] is { } p ? (int)p : 0);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
