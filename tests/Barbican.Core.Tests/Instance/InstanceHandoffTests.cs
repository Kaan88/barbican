using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Instance;

namespace Barbican.Core.Tests.Instance;

/// <summary>The 3 tests of Rust src/instance.rs, ported (M3 stream composer-ui) on loopback sockets with port 0 only; nothing leaves the machine. D-241.</summary>
public sealed class InstanceHandoffTests
{
    private static string? Arg(params string[] args) => InstanceHandoff.MailtoArg(args);

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    [RustTest("instance::args_and_requests")]
    public void Args_and_requests()
    {
        Assert.Equal("mailto:a@x.example?subject=Hi", Arg("--flag", "\"mailto:a@x.example?subject=Hi\""));
        Assert.Null(Arg("http://x"));
        Assert.Equal("mailto:a@x.example", InstanceHandoff.ReadRequest(Bytes("tok\nmailto:a@x.example\n"), "tok"));
        Assert.Null(InstanceHandoff.ReadRequest(Bytes("bad\nmailto:a@x.example\n"), "tok"));
        Assert.Null(InstanceHandoff.ReadRequest(Bytes("tok\nfile:///etc/passwd\n"), "tok"));
    }

    [Fact]
    [RustTest("instance::handoff_over_loopback")]
    public void Handoff_over_loopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cts = new CancellationTokenSource();
        var idle = new List<TcpClient>();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var got = new BlockingCollection<string>();
            _ = InstanceHandoff.ServeAsync(listener, "secret", "barbican hi", got.Add, cts.Token);
            HandoffEndpoint E(string token, string greet) => new(port, token, greet, 0);
            Assert.False(InstanceHandoff.Send(E("wrong", "barbican hi"), "mailto:a@x.example"));

            // a server that does not greet as expected gets nothing
            Assert.False(InstanceHandoff.Send(E("secret", "barbican other"), "mailto:a@x.example"));
            Assert.False(got.TryTake(out _));

            // idle connections do not block a real handoff
            for (var i = 0; i < 3; i++)
            {
                var c = new TcpClient();
                c.Connect(IPAddress.Loopback, port);
                idle.Add(c);
            }

            Assert.True(InstanceHandoff.Send(E("secret", "barbican hi"), "mailto:a@x.example?subject=Hand%20off"));
            Assert.True(got.TryTake(out var url, TimeSpan.FromSeconds(3)));
            Assert.Equal("mailto:a@x.example?subject=Hand%20off", url);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            idle.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    [RustTest("instance::foreign_program_on_a_stale_port_gets_nothing")]
    public async Task Foreign_program_on_a_stale_port_gets_nothing()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            stream.ReadTimeout = 1500;
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\n"));
            var buffer = new byte[4096];
            var total = 0;
            try
            {
                int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    total += n;
                }
            }
            catch (IOException)
            {
                // the read deadline: the stale-port client sent nothing
            }

            return total;
        });
        try
        {
            Assert.False(InstanceHandoff.Send(new HandoffEndpoint(port, "secret", "barbican hi", 0), "mailto:a@x.example?body=private"));
            Assert.Equal(0, await received);
        }
        finally
        {
            listener.Stop();
            _ = received.ContinueWith(static _ => { }, TaskScheduler.Default);
        }
    }
}
