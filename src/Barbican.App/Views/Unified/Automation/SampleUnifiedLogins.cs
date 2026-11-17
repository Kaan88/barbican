#if AUTOMATION
// Mirrors: M2 plan 6.5 hidden scenario ("FakeJmap holding two accounts": unified on, star a row of the second account, check the request log).
// Owner unified. A sample-data run (BARBICAN_SAMPLE=1) has one in-process FakeJmap login (SampleActiveMail); this adds a second in-process
// FakeJmap login, "sample-2", with its own inbox, so the unified inbox reads two accounts. Every request of either login is logged per login
// (`unified log`), which is how the scenario proves that an action on a row of the second account went only to that account.
// Nothing here leaves the process: both servers are FakeJmap in memory.

using System.Collections.Concurrent;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Service;
using Barbican.FakeJmap;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Unified;

namespace Barbican.App.Views.Unified.Automation;

/// <summary>The two logins of a sample run.</summary>
public sealed class SampleUnifiedLogins : IUnifiedLogins, IDisposable
{
    /// <summary>The key of the second sample login.</summary>
    public const string Second = "sample-2";

    private readonly IActiveMail _active;
    private readonly FakeJmapServer _second = new(new FakeJmapOptions { Origin = "https://second.example.net", Username = "ben@example.net", AccountId = "b2" }, seed: false);
    private readonly Lazy<IJmapClient?> _first;
    private readonly IJmapClient _secondClient;
    private readonly ConcurrentQueue<(string Login, string Methods)> _log = new();

    public SampleUnifiedLogins(IActiveMail active, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(time);
        _active = active;
        var inbox = _second.Store.AddMailbox("Inbox", "inbox");
        _second.Store.AddMailbox("Archive", "archive");
        _second.Store.AddMailbox("Trash", "trash");
        var now = time.GetUtcNow();
        string[] subjects = ["Second account: trip itinerary", "Second account: invoice 2026-11", "Second account: team lunch", "Second account: weekly report"];
        for (var i = 0; i < subjects.Length; i++)
        {
            _second.Store.Deliver(new FakeMail { Subject = subjects[i], FromName = "Ben's colleague", FromEmail = "colleague@example.net", To = "ben@example.net", ReceivedAt = now.AddMinutes(-7 - (i * 90)), Text = "Sample message of the second login." }, inbox);
        }

        _first = new Lazy<IJmapClient?>(() => (_active.Mail as MailService)?.Client is { } c ? new LoggedJmapClient(c, m => _log.Enqueue((_active.AccountKey ?? "sample", m))) : null);
#pragma warning disable VSTHRD002, MA0045 // the fake is in memory: connecting finishes in milliseconds (as SampleActiveMail)
        _secondClient = new LoggedJmapClient(Task.Run(() => FakeJmapHarness.ConnectAsync(_second, time)).GetAwaiter().GetResult(), m => _log.Enqueue((Second, m)));
#pragma warning restore VSTHRD002, MA0045
    }

    /// <inheritdoc />
    public string? Active => _active.AccountKey;

    /// <inheritdoc />
    public IReadOnlyList<(string Login, IJmapClient? Client)> Logins => Active is { } a ? [(a, _first.Value), (Second, _secondClient)] : [];

    /// <summary>The requests each login sent, oldest first ("Email/query,Email/get").</summary>
    public IReadOnlyList<(string Login, string Methods)> Log => [.. _log];

    /// <inheritdoc />
    public IJmapClient? ClientOf(string login) =>
        string.Equals(login, Second, StringComparison.Ordinal) ? _secondClient
        : string.Equals(login, Active, StringComparison.Ordinal) ? _first.Value
        : null;

    /// <inheritdoc />
    public bool IsTestServer(string login) => ClientOf(login) is not null;

    public void Dispose() => _second.Dispose();
}
#endif
