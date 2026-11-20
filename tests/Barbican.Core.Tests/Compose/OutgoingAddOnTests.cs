// Bulwark lib/outgoing/outgoing-keywords.ts and components/email/email-composer.tsx send hooks.
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Tests.Compose;

public sealed class OutgoingAddOnTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SentCopyKeepsHostKeywordsAndOnlyPermittedPluginKeywords()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var draft = PipelineRig.NewDraft() with { ComposerId = "composer-1", Keywords = ["$label:case"], AuthoredText = "written" };
        var sent = await rig.Sender.SendAsync(draft, new SendOptions(), Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        var keywords = rig.Store.Email(sent.Value!.EmailId)!["keywords"]!;
        Assert.True(keywords["$seen"]!.GetValue<bool>());
        Assert.True(keywords["$label:case"]!.GetValue<bool>());
        Assert.True(keywords["$flagged"]!.GetValue<bool>());
        Assert.True(keywords["case-1"]!.GetValue<bool>());
        Assert.Null(keywords["$answered"]);
        Assert.Equal("composer-1", hooks.Checks[0].ComposerId, StringComparer.Ordinal);
        Assert.Equal("written", hooks.Checks[0].AuthoredText, StringComparer.Ordinal);
        Assert.Equal(2, hooks.Checks.Count);
        Assert.Equal("audit@example.net", hooks.Checks[1].Bcc, StringComparer.Ordinal);
        Assert.NotNull(hooks.Session);
        Assert.Equal(1, hooks.StartCount);
        Assert.Equal(1, hooks.IssueCount);
    }

    [Fact]
    public async Task ChangedRecipientsMustPassSecondCheck()
    {
        var hooks = new Hooks { RejectSecond = true };
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions(), Ct);
        Assert.False(sent.IsOk);
        Assert.Empty(rig.Api.Submissions);
    }

    [Fact]
    public async Task RevokedAuthorityAtIssueDoesNotCreateEmailOrSubmission()
    {
        var hooks = new Hooks { DenyIssue = true };
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions(), Ct);
        Assert.False(sent.IsOk);
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(rig.InFolder("drafts"));
        Assert.Equal(1, hooks.IssueCount);
    }

    [Fact]
    public void KeywordValidationIsBoundedAndCaseInsensitive()
    {
        var values = OutgoingKeywords.Clean(["ONE", "one", "$seen", "$flagged", "$label:", "$label:x", "bad space", "bad/ok", "bad]", .. Enumerable.Range(0, 20).Select(i => "k" + i)]);
        Assert.Equal(10, values.Count);
        Assert.Equal(["ONE", "$flagged", "$label:x", "bad/ok"], values.Take(4), StringComparer.Ordinal);
        Assert.DoesNotContain("$seen", values, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Hooks : IOutgoingAddOnHooks, IOutgoingAddOnSend
    {
        public List<OutgoingEmailPayload> Checks { get; } = [];
        public IAccountSession? Session { get; private set; }
        public int StartCount { get; private set; }
        public int IssueCount { get; private set; }
        public bool RejectSecond { get; init; }
        public bool DenyIssue { get; init; }
        public IOutgoingAddOnSend Start(IAccountSession sendingSession)
        {
            StartCount++; Session = sendingSession; return this;
        }
        public Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct)
        {
            Checks.Add(outgoing);
            return Task.FromResult(!RejectSecond || Checks.Count == 1);
        }
        public Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct) =>
            Task.FromResult(outgoing with { AccountId = "forged", IdentityId = "forged", ComposerId = "forged", Bcc = "audit@example.net", Keywords = ["case-1", "$flagged", "$seen", "$answered"] });
        public Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct)
        {
            IssueCount++;
            return Task.FromResult(!DenyIssue && issue());
        }
    }
}
