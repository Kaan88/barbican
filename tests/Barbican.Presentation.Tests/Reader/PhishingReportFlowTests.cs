// Bulwark components/email/__tests__/phishing-report-flow.test.tsx and phishing-report-menus.test.tsx: asks nothing while the admin set no address, names the
// address and sends nothing on Cancel, sends one report per message, moves each to Junk with $phishing (read), Undo moves them back, a report that fails
// moves nothing, own mail and already reported mail are skipped; the row menu offers "Report phishing" only once an address is set; the banner follows $phishing.
// The report goes to a recording fake: nothing is sent anywhere (the JMAP send itself is PhishingReporterTests in Core).

#pragma warning disable MA0002, MA0006, MA0048, VSTHRD003 // ids and texts are compared ordinally by nature; the scripted fakes sit beside their tests
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;
using Barbican.Core.OrganizationPolicy;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Tests.Actions;
using Barbican.Presentation.Tests.Contacts;
using Barbican.Presentation.Tests.Menus;

namespace Barbican.Presentation.Tests.Reader;

public sealed class PhishingReportFlowTests
{
    private const string Admin = "security@example.org";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_is_asked_or_sent_while_the_admin_set_no_address()
    {
        using var rig = await Rig.StartAsync(on: false);
        var targets = rig.Harness.Pick(0);

        Assert.False(rig.Flow.CanReport(targets, "inbox"));
        Assert.True((await rig.Flow.RunAsync(targets, "inbox", Ct)).IsOk);

        Assert.Empty(rig.Dialogs.Shown);
        Assert.Empty(rig.Reporter.Reported);
        Assert.Equal("Nothing to report.", rig.Flow.LastResult);
    }

    [Fact]
    public async Task Cancel_names_the_address_and_sends_and_moves_nothing()
    {
        using var rig = await Rig.StartAsync();
        var targets = rig.Harness.Pick(0);
        rig.Dialogs.Answer(null);

        await rig.Flow.RunAsync(targets, "inbox", Ct);

        var asked = Assert.IsType<ConfirmDialogRequest>(Assert.Single(rig.Dialogs.Shown));
        Assert.Equal("Report phishing?", asked.Title);
        Assert.Equal($"The message goes to {Admin} and moves to Junk.", asked.Message);
        Assert.Empty(rig.Reporter.Reported);
        Assert.Equal([rig.Harness.Folder("inbox")], rig.Boxes(targets[0].EmailId));
    }

    [Fact]
    public async Task One_report_per_message_each_moved_to_Junk_with_the_mark_and_read_and_Undo_moves_them_back()
    {
        using var rig = await Rig.StartAsync();
        var targets = rig.Harness.Pick(0, 1);
        rig.Dialogs.Answer(true);

        await rig.Flow.RunAsync(targets, "inbox", Ct);

        Assert.Equal($"Each of the 2 messages goes to {Admin} as its own report and moves to Junk.", Assert.IsType<ConfirmDialogRequest>(rig.Dialogs.Shown[0]).Message);
        Assert.Equal(targets.Select(t => t.EmailId), rig.Reporter.Reported.Select(r => r.Message.EmailId));
        Assert.All(rig.Reporter.Reported, r => Assert.Equal(new PhishingReportPolicy(Admin, PhishingReportFormat.Forward), r.Setup));
        foreach (var t in targets)
        {
            Assert.Equal([rig.Harness.Folder("junk")], rig.Boxes(t.EmailId));
            Assert.Equal("true", rig.Keyword(t.EmailId, "$phishing"));
            Assert.Equal("true", rig.Keyword(t.EmailId, "$junk"));
            Assert.Equal("true", rig.Keyword(t.EmailId, "$seen"));
        }

        var toast = rig.Harness.Toasts.Toasts.Last();
        Assert.Equal($"2 messages reported to {Admin} and moved to Junk.", toast.Message);
        Assert.Equal("Undo", toast.ActionLabel);
        Assert.True(await rig.Harness.Undo.UndoLatestAsync(Ct));
        foreach (var t in targets)
        {
            Assert.Equal([rig.Harness.Folder("inbox")], rig.Boxes(t.EmailId));
            Assert.Equal(string.Empty, rig.Keyword(t.EmailId, "$phishing"));
            Assert.Equal(string.Empty, rig.Keyword(t.EmailId, "$junk"));
        }

        // the report itself stays sent
        Assert.Equal(2, rig.Reporter.Reported.Count);
    }

    [Fact]
    public async Task A_report_that_fails_moves_nothing_for_that_message_and_says_so()
    {
        using var rig = await Rig.StartAsync();
        var targets = rig.Harness.Pick(0, 1);
        rig.Reporter.Fail.Add(targets[0].EmailId);
        rig.Dialogs.Answer(true);

        await rig.Flow.RunAsync(targets, "inbox", Ct);

        Assert.Equal([rig.Harness.Folder("inbox")], rig.Boxes(targets[0].EmailId));
        Assert.Equal([rig.Harness.Folder("junk")], rig.Boxes(targets[1].EmailId));
        Assert.Contains(rig.Harness.Toasts.Toasts, t => t.IsError && t.Message.StartsWith("The message couldn't be reported.", StringComparison.Ordinal));
        Assert.Equal("Reported 1, failed 1.", rig.Flow.LastResult);
    }

    [Fact]
    public async Task When_every_report_fails_nothing_is_moved_and_no_move_toast_shows()
    {
        using var rig = await Rig.StartAsync();
        var targets = rig.Harness.Pick(0);
        rig.Reporter.Fail.Add(targets[0].EmailId);
        rig.Dialogs.Answer(true);

        await rig.Flow.RunAsync(targets, "inbox", Ct);

        Assert.Equal([rig.Harness.Folder("inbox")], rig.Boxes(targets[0].EmailId));
        Assert.DoesNotContain(rig.Harness.Toasts.Toasts, t => t.Message.Contains("moved to Junk", StringComparison.Ordinal));
        Assert.Equal("Not reported.", rig.Flow.LastResult);
    }

    [Fact]
    public async Task Own_mail_drafts_and_an_already_reported_message_are_skipped()
    {
        using var rig = await Rig.StartAsync();
        var one = rig.Harness.Pick(0)[0];
        var sent = one with { MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { [rig.Harness.Folder("sent")] = true } };
        var draft = one with { Keywords = Keywords.Of("$draft") };
        var reported = one with { Keywords = Keywords.Of("$phishing") };

        Assert.False(rig.Flow.CanReport([sent], "all"));
        Assert.False(rig.Flow.CanReport([one], "sent"));
        Assert.False(rig.Flow.CanReport([draft], "inbox"));
        Assert.False(rig.Flow.CanReport([reported], "junk"));
        Assert.True(rig.Flow.CanReport([one, sent], "inbox"));
        await rig.Flow.RunAsync([sent, draft, reported], "inbox", Ct);
        Assert.Empty(rig.Dialogs.Shown);
    }

    [Fact]
    public async Task The_command_runs_the_flow_and_is_off_without_an_address()
    {
        using var rig = await Rig.StartAsync();
        var targets = rig.Harness.Pick(0);
        var command = new DelegateAppCommand(CommandIds.ReportPhishing, c => rig.Flow.CanReport(c.Targets, c.Mailbox?.Role), (c, ct) => rig.Flow.RunAsync(c.Targets, c.Mailbox?.Role, ct));
        var context = new CommandContext(CommandSurface.ContextMenu, targets, null, null, "acct");
        rig.Dialogs.Answer(true);

        Assert.True(command.CanExecute(context));
        Assert.True((await command.ExecuteAsync(context, Ct)).IsOk);
        Assert.Single(rig.Reporter.Reported);
        rig.Setups.Policy = PhishingReportPolicy.Off;
        Assert.False(command.CanExecute(context));
    }

    [Fact]
    public void The_row_menu_offers_Report_phishing_only_once_an_address_is_set_and_not_for_reported_mail()
    {
        var data = new FakeMenuData();
        var target = new MailTarget("acct", string.Empty, "m1", "t1", "m1", new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);
        MenuContext Context(MailTarget t) => new(MenuIds.Mail, [t], new MailboxRef("acct", string.Empty, "inbox", "inbox"), null, null, null, CommandSurface.ContextMenu);

        Assert.DoesNotContain(MailMenus.Main(Context(target), data), r => r.CommandId == CommandIds.ReportPhishing);
        data.PhishingAccounts.Add("acct");
        var rows = MailMenus.Main(Context(target), data);
        var row = rows.Single(r => r.CommandId == CommandIds.ReportPhishing);
        Assert.Equal("Report phishing…", row.Label);
        Assert.True(rows.ToList().IndexOf(row) == rows.ToList().FindIndex(r => r.CommandId == CommandIds.ReportSpam) + 1);
        Assert.DoesNotContain(MailMenus.Main(Context(target with { Keywords = Keywords.Of("$phishing") }), data), r => r.CommandId == CommandIds.ReportPhishing);
    }

    [Fact]
    public async Task The_banner_shows_for_a_message_with_the_mark_and_not_for_others()
    {
        var source = new PhishingMarkBannerSource();
        var reported = new ReaderSnapshot { EmailId = "e1", Body = new EmailBody { Id = "e1", Keywords = Keywords.Of("$phishing", "$seen") } };
        var plain = new ReaderSnapshot { EmailId = "e2", Body = new EmailBody { Id = "e2", Keywords = Keywords.Of("$junk") } };

        var banner = Assert.Single(await CollectAsync(source, reported));

        Assert.Equal("phishing-mark", banner.Id);
        Assert.Equal("You reported this message as phishing. Its links are turned off.", banner.Title);
        Assert.True(banner.IsWarning);
        Assert.Empty(banner.Actions);
        Assert.Empty(await CollectAsync(source, plain));
        Assert.Empty(await CollectAsync(source, ReaderSnapshot.Empty));
    }

    private static async Task<List<ReaderBanner>> CollectAsync(PhishingMarkBannerSource source, ReaderSnapshot snapshot)
    {
        var all = new List<ReaderBanner>();
        await foreach (var banner in source.BannersForAsync(snapshot, Ct))
        {
            all.Add(banner);
        }

        return all;
    }

    private sealed class Rig : IDisposable
    {
        private Rig(ActionHarness harness, bool on)
        {
            Harness = harness;
            Setups = new FakeSetups { Policy = on ? new PhishingReportPolicy(Admin, PhishingReportFormat.Forward) : PhishingReportPolicy.Off };
            Flow = new PhishingReportFlow(Setups, Reporter, harness.Actions, Dialogs, harness.Sidebar, harness.Toasts);
        }

        public ActionHarness Harness { get; }

        public FakeSetups Setups { get; }

        public FakeReporter Reporter { get; } = new();

        public ScriptedDialogs Dialogs { get; } = new();

        public PhishingReportFlow Flow { get; }

        public static async Task<Rig> StartAsync(bool on = true)
        {
            var harness = await ActionHarness.StartAsync();
            await harness.OpenAsync("inbox");
            return new Rig(harness, on);
        }

        public string[] Boxes(string id) => [.. ((System.Text.Json.Nodes.JsonObject)Harness.Server.Store.Email(id)!["mailboxIds"]!).Select(kv => kv.Key)];

        public string Keyword(string id, string keyword) => Harness.Server.Store.Email(id)!["keywords"]![keyword]?.ToJsonString() ?? string.Empty;

        public void Dispose() => Harness.Dispose();
    }

    private sealed class FakeSetups : IPhishingReportSetups
    {
        public PhishingReportPolicy Policy { get; set; } = PhishingReportPolicy.Off;

        public PhishingReportPolicy Cached(string accountKey) => Policy;

        public Task<PhishingReportPolicy> ReadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Policy);
    }

    private sealed class FakeReporter : IPhishingReporter
    {
        public List<(MailTarget Message, PhishingReportPolicy Setup)> Reported { get; } = [];

        public HashSet<string> Fail { get; } = new(StringComparer.Ordinal);

        public Task<Result<Unit>> ReportAsync(MailTarget message, PhishingReportPolicy setup, CancellationToken ct)
        {
            if (Fail.Contains(message.EmailId))
            {
                return Task.FromResult(Result<Unit>.Fail(new ValidationError("The server refused it.")));
            }

            Reported.Add((message, setup));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }
}
