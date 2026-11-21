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
    private readonly FakeJmapServer _server = new(Options());
    private readonly Barbican.Core.Jmap.Transport.JmapClient _client;
#if WINDOWS
    private readonly SamplePolicyFixture? _policyFixture;
#endif

    private static FakeJmapOptions Options()
    {
        var options = new FakeJmapOptions { SieveCapability = true };
#if WINDOWS
        if (Barbican.Core.Storage.ProfilePaths.IsAutomationProfile(Environment.GetEnvironmentVariable("BARBICAN_PROFILE")) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_ADDON_ACCOUNT"), "1", StringComparison.Ordinal) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_SEND_PIPELINE"), "1", StringComparison.Ordinal))
            return options with { SubmissionExtensions = ["FUTURERELEASE"], RfcSubmissionFollowupResponse = true };
#endif
        return options;
    }

    public SampleActiveMail(IHookBus hooks, TimeProvider time, Barbican.Core.Storage.IProfilePaths paths)
    {
        // G-049/G-050: one synthetic message whose link text names one site while the address goes to another, for a hidden phishing-report run
        if (Barbican.Core.Storage.ProfilePaths.IsAutomationProfile(Environment.GetEnvironmentVariable("BARBICAN_PROFILE")) &&
            Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PHISHING_REPORT") is "1" or "arf")
        {
            _server.Store.Deliver(new FakeMail
            {
                Subject = "Verify your account now",
                FromEmail = "support@bank.example.net",
                FromName = "Bank Support",
                ReceivedAt = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero),
                Text = "Sign in at https://bank.example.org/account",
                Html = "<p>Please <a href=\"https://evil.example.net/login\">https://bank.example.org/account</a> within 24 hours.</p><p><a href=\"https://example.com/help\">Help</a></p>",
            });
        }

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
#if WINDOWS
        if (SamplePolicyFixture.Enabled(paths))
        {
            _policyFixture = new SamplePolicyFixture(_server, client, paths, time);
            client.Dispose();
            client = _policyFixture.Client;
        }
#endif
        _client = client;
        var service = new MailService(client, time);
        Mail = service;
        Sync = new Barbican.Core.Sync.MailSync(service, hooks, time);
        AccountKey = "sample";
        AccountUser = _server.Options.Username;
    }

    public void Dispose()
    {
#if WINDOWS
        _policyFixture?.Dispose();
#endif
        _server.Dispose();
    }

    /// <summary>The sample account is writable (the fake server is in memory); every write of a sample run goes to it.</summary>
    public Barbican.Core.Mail.Write.IMailWriter? WriterOf(string accountKey) => new Barbican.Core.Mail.Write.MailWriter(_client, _server.Options.AccountId);

    /// <inheritdoc />
    public bool CanWrite(string accountKey) => true;

    public string? AccountKey { get; }

    public string? AccountUser { get; }

    public IMailService? Mail { get; }

    public IMailSync? Sync { get; }

#if WINDOWS
    internal string ReadRawState()
    {
        var sent = _server.Store.MailboxId("sent");
        var drafts = _server.Store.MailboxId("drafts");
        var emails = _server.Store.EmailIds().Select(_server.Store.Email).Where(e => e is not null).ToArray();
        var submissions = _server.Api.SubmissionStates;
        var submitted = _server.Api.Submissions;
        var uploadedId = _server.LastUploadedBlobId;
        var upload = uploadedId is null ? null : _server.Store.Blob(uploadedId);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            submissions = submissions.Count,
            retainedSent = emails.Count(e => string.Equals((string?)e!["subject"], "Synthetic raw sent", StringComparison.Ordinal) && sent is not null && e["mailboxIds"]?[sent] is not null),
            takeoverSent = emails.Count(e => ((string?)e!["subject"])?.EndsWith("Synthetic takeover send", StringComparison.Ordinal) == true && sent is not null && e["mailboxIds"]?[sent] is not null),
            takeoverAbort = emails.Count(e => ((string?)e!["subject"])?.EndsWith("Synthetic takeover abort", StringComparison.Ordinal) == true),
            takeoverFalse = emails.Count(e => ((string?)e!["subject"])?.EndsWith("Synthetic takeover false", StringComparison.Ordinal) == true),
            takeoverContinue = emails.Count(e => ((string?)e!["subject"])?.EndsWith("Synthetic takeover continue", StringComparison.Ordinal) == true && sent is not null && e["mailboxIds"]?[sent] is not null),
            draftTakeover = emails.Count(e => ((string?)e!["subject"])?.EndsWith("Synthetic takeover send", StringComparison.Ordinal) == true && drafts is not null && e["mailboxIds"]?[drafts] is not null),
            retainedSubmitOnly = emails.Count(e => string.Equals((string?)e!["subject"], "Synthetic raw submit only", StringComparison.Ordinal)),
            lastEnvelopeRecipient = submitted.Count == 0 ? null : (string?)submitted[^1]["envelope"]?["rcptTo"]?[0]?["email"],
            lastUpload = upload is null ? null : System.Text.Encoding.UTF8.GetString(upload),
        });
    }
    internal string ReadSentKeywords(string subject)
    {
        var sent = _server.Store.MailboxId("sent");
        if (sent is null) return "error no sent mailbox";
        var matches = _server.Store.EmailIds().Select(_server.Store.Email).Where(e => e is not null && string.Equals((string?)e["subject"], subject, StringComparison.Ordinal) && e["mailboxIds"]?[sent] is not null).ToArray();
        if (matches.Length != 1) return "error expected exactly one sent message; found " + matches.Length;
        return System.Text.Json.JsonSerializer.Serialize(new { subject, keywords = (matches[0]!["keywords"] as System.Text.Json.Nodes.JsonObject)?.Select(k => k.Key).Order(StringComparer.Ordinal).ToArray() ?? [] });
    }

    internal Barbican.Core.Accounts.IAccountSession CreateAddOnSession() =>
        new Barbican.Core.Accounts.AccountSession(new("sample", _server.Options.Username, _server.Options.Origin),
            _client, Mail!, Sync!, tokens: null, allowsAddOnStorage: true);
#endif

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
#endif
