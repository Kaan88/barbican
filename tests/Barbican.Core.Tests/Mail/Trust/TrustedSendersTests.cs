// I-209: trusted senders are kept locally (D-3), case-insensitive, and survive a restart; a broken file trusts nobody.

using Barbican.Core.Mail.Trust;

namespace Barbican.Core.Tests.Mail.Trust;

public sealed class TrustedSendersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-trust-" + Guid.NewGuid().ToString("N"));

    public TrustedSendersTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task A_trusted_sender_is_kept_across_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var trust = new TrustedSenders(_dir);
        Assert.True((await trust.AddAsync("Anna@Shop.example.com", ct)).IsOk);
        Assert.True(trust.IsTrusted("anna@shop.example.com"));
        Assert.False(trust.IsTrusted("bob@shop.example.com"));
        Assert.False((await trust.AddAsync("not an address", ct)).IsOk);

        var next = new TrustedSenders(_dir);
        for (var i = 0; i < 100 && !next.IsLoaded; i++)
        {
            await Task.Delay(10, ct);
        }

        Assert.Equal(["anna@shop.example.com"], next.All);
        Assert.True((await next.RemoveAsync("ANNA@shop.example.com", ct)).IsOk);
        Assert.Empty(next.All);
    }

    [Fact]
    public async Task A_broken_file_trusts_nobody()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "trusted-senders.json"), "{ nope", TestContext.Current.CancellationToken);
        var trust = new TrustedSenders(_dir);
        for (var i = 0; i < 100 && !trust.IsLoaded; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Empty(trust.All);
    }
}
