#if AUTOMATION
// Mirrors: M1-plan §6.4 (screenshots with sample data, FakeJmap seed only). The mail services of an in-process FakeJmapServer, so hidden runs of the UI never touch a real mailbox.

using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;
using Barbican.Presentation.Mail;

namespace Barbican.App.Shell.Automation;

/// <summary>The active mail of a sample-data run: the real MailService and MailSync over the fake server's seed.</summary>
public sealed class SampleActiveMail : IActiveMail, Barbican.Presentation.Actions.IMailWriterSource, IDisposable
{
    private readonly FakeJmapServer _server = new(new FakeJmapOptions { SieveCapability = true });
    private readonly Barbican.Core.Jmap.Transport.JmapClient _client;

    public SampleActiveMail(IHookBus hooks, TimeProvider time)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_LONG_THREAD"), "1", StringComparison.Ordinal))
        {
            var parent = _server.Store.EmailIds().First(id => string.Equals((string?)_server.Store.Email(id)?["subject"], FakeSeed.ThreadSubject, StringComparison.Ordinal));
            for (var i = 6; i <= 8; i++)
            {
                parent = _server.Store.Deliver(new FakeMail
                {
                    Subject = "Re: " + FakeSeed.ThreadSubject,
                    FromEmail = "planner@example.org",
                    FromName = "Planner",
                    ReceivedAt = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero).AddHours(i * 6),
                    Text = i == 6
                        ? "Message 6 of the planning thread.\n" + string.Join("\n", Enumerable.Range(1, 12).Select(n => $"Planning item {n}."))
                        : $"Message {i} of the planning thread.",
                    Cc = i == 6 ? ["team@example.org"] : [],
                    ReplyTo = parent,
                });
            }
        }

        // The fake is in memory: connecting finishes in milliseconds and happens once, while the composition root is built.
#pragma warning disable VSTHRD002, MA0045 // synchronous by design: IActiveMail has no async member and the fake never leaves the process
        var client = Task.Run(() => FakeJmapHarness.ConnectAsync(_server, time)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002, MA0045
        _client = client;
        var service = new MailService(client, time);
        Mail = service;
        Sync = new Barbican.Core.Sync.MailSync(service, hooks, time);
        AccountKey = "sample";
        AccountUser = _server.Options.Username;
    }

    public void Dispose() => _server.Dispose();

    /// <summary>The sample account is writable (the fake server is in memory); every write of a sample run goes to it.</summary>
    public Barbican.Core.Mail.Write.IMailWriter? WriterOf(string accountKey) => new Barbican.Core.Mail.Write.MailWriter(_client, _server.Options.AccountId);

    /// <inheritdoc />
    public bool CanWrite(string accountKey) => true;

    public string? AccountKey { get; }

    public string? AccountUser { get; }

    public IMailService? Mail { get; }

    public IMailSync? Sync { get; }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
#endif
