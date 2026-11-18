#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
// M3 close, task 2: the composer end to end on the REAL pipeline over FakeJmap: the real editor session types (the same session calls the editor probes make), a recipient chip is added, Send with the undo window, Undo, the draft reopens
// from the server, and the real send stores the message whose HTML and text are checked. Scenario record: spec/feel/M3/compose-roundtrip.md.

using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Tests.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Tests.Compose.Pipeline;

namespace Barbican.Presentation.Tests.Compose.EndToEnd;

public sealed class ComposeRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(ComposerViewModel Composer, List<ComposeSeed> Reopened)> OpenAsync(PipelineRig rig, RecordingToasts toasts, ComposeSeed seed, int delay)
    {
        var reopened = new List<ComposeSeed>();
        var ctx = new ComposerContext(rig.Drafts, rig.Uploader, new DirectComposerSender(rig.Sender), new EditorViewModelFactory(new EditorSessionFactory(), new NoSpellEngine(), new NoRewriteProvider()), new EmptyRecipientSources(), rig.Hooks, rig.Clock, new ComposeSettings { SendDelaySeconds = delay })
        {
            Toasts = toasts,
            Reopen = (s, _) =>
            {
                reopened.Add(s);
                return Task.CompletedTask;
            },
        };
        var ids = (await rig.Identities.ListAsync(PipelineRig.Account, Ct)).Value!;
        return (new ComposerViewModel(ctx, seed, ids), reopened);
    }

    [Fact]
    public async Task Type_chip_send_with_undo_undo_reopen_the_draft_and_send_for_real()
    {
        var rig = await PipelineRig.StartAsync(codec: new DocHtmlCodec());
        var toasts = new RecordingToasts();
        var sentBefore = rig.InFolder("sent").ToHashSet();
        var (composer, reopened) = await OpenAsync(rig, toasts, new ComposeSeed { AccountId = PipelineRig.Account, Kind = ComposeKind.New, Body = Doc.FromText(string.Empty) }, delay: 10);

        // the real editor session types (what editor-type, editor-key and editor-cmd do in the hidden app)
        var s = composer.Editor!.Session;
        s.InsertText("Hello ");
        s.ToggleMark(Mark.Bold);
        s.InsertText("world");
        s.ToggleMark(Mark.Bold);
        s.Enter(shift: false);
        s.InsertText("2 < 3 & \"quotes\"");
        Assert.False(s.Doc.Blocks[1].Runs[0].Marks.Bold, "text typed after toggling bold off and Enter is plain");
        composer.Subject = "Round trip";
        composer.To.Edited("anna@example.org");
        Assert.True(composer.To.Key("Enter", false) || composer.To.Chips.Count == 1);
        composer.To.Commit();
        Assert.Equal("anna@example.org", Assert.Single(composer.To.Chips).Full);

        // send with the undo window: held on the server, an Undo toast offered
        Assert.True(await composer.SendAsync());
        var held = toasts.Shown.Single(t => t.ActionLabel == "Undo");
        Assert.Single(rig.Api.Submissions);
        Assert.Equal("pending", Assert.Single(rig.Api.SubmissionStates).UndoStatus);
        Assert.Single(rig.InFolder("sent").Except(sentBefore)); // a held message is filed in Sent at submission (onSuccessUpdateEmail) and comes back to Drafts on undo

        // undo: the submission is cancelled and the composer comes back with everything typed
        await held.Action!(Ct);
        var seed = Assert.Single(reopened);
        Assert.DoesNotContain(rig.Api.SubmissionStates, x => x.UndoStatus == "pending"); // cancelled (and removed) on the server
        Assert.Equal("Round trip", seed.Subject);
        Assert.Contains("anna@example.org", seed.To, StringComparison.Ordinal);
        Assert.Contains("Hello world", DocHtml.ToText(seed.Body), StringComparison.Ordinal);
        Assert.NotNull(seed.DraftId);
        Assert.Empty(rig.InFolder("sent").Except(sentBefore));
        var draft = Assert.Single(rig.InFolder("drafts"));
        Assert.Equal(seed.DraftId, draft);

        // reopen the draft from the server, as the Drafts folder does
        var fromServer = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.EditDraft) { SourceEmailId = draft }, Ct)).Value!;
        Assert.Equal("Round trip", fromServer.Subject);
        Assert.Equal("Hello world\n2 < 3 & \"quotes\"", DocHtml.ToText(fromServer.Body).Trim());
        var (again, _) = await OpenAsync(rig, new RecordingToasts(), fromServer, delay: 0);
        Assert.Equal("anna@example.org", Assert.Single(again.To.Chips).Full);
        Assert.Contains(again.Editor!.Session.Doc.Blocks[0].Runs, r => r.Marks.Bold && r.Text == "world");

        // send for real
        Assert.True(await again.SendAsync());
        CheckStored(rig, sentBefore);
    }

    private static void CheckStored(PipelineRig rig, HashSet<string> sentBefore)
    {
        var sent = Assert.Single(rig.InFolder("sent").Except(sentBefore));
        Assert.Empty(rig.InFolder("drafts"));
        var email = rig.Store.Email(sent)!;
        Assert.Equal("Round trip", (string?)email["subject"]);
        Assert.Equal("anna@example.org", (string?)email["to"]![0]!["email"]);
        var values = email["bodyValues"]!.AsObject().Select(kv => (string?)kv.Value!["value"]).ToList();
        var html = values.SingleOrDefault(v => v!.Contains("<strong", StringComparison.Ordinal)) ?? throw new Xunit.Sdk.XunitException(string.Join(" ||| ", values));
        Assert.Contains("Hello <strong>world</strong>", html, StringComparison.Ordinal);
        Assert.Contains("2 &lt; 3 &amp; &quot;quotes&quot;", html.Replace("&#34;", "&quot;", StringComparison.Ordinal), StringComparison.Ordinal);
        var text = values.Single(v => v!.StartsWith("Hello world", StringComparison.Ordinal))!;
        Assert.Equal("Hello world\n2 < 3 & \"quotes\"", text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
        Assert.Null(email["keywords"]!["$draft"]);
        Assert.IsType<JsonObject>(email);
    }
}
