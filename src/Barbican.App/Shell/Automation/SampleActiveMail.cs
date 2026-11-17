#if AUTOMATION
// Mirrors: M1-plan §6.4 (screenshots with sample data, FakeJmap seed only). The mail services of an in-process FakeJmapServer, so hidden runs of the UI never touch a real mailbox.

using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;
using Barbican.Presentation.Mail;

namespace Barbican.App.Shell.Automation;

/// <summary>The active mail of a sample-data run: the real MailService and MailSync over the fake server's seed.</summary>
public sealed class SampleActiveMail : IActiveMail, IDisposable
{
    private readonly FakeJmapServer _server = new();

    public SampleActiveMail(IHookBus hooks, TimeProvider time)
    {
        // The fake is in memory: connecting finishes in milliseconds and happens once, while the composition root is built.
#pragma warning disable VSTHRD002, MA0045 // synchronous by design: IActiveMail has no async member and the fake never leaves the process
        var client = Task.Run(() => FakeJmapHarness.ConnectAsync(_server, time)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002, MA0045
        var service = new MailService(client, time);
        Mail = service;
        Sync = new Barbican.Core.Sync.MailSync(service, hooks, time);
        AccountKey = "sample";
        AccountUser = _server.Options.Username;
    }

    public void Dispose() => _server.Dispose();

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
