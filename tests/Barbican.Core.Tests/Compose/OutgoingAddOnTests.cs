// Bulwark lib/outgoing/outgoing-keywords.ts and components/email/email-composer.tsx send hooks.
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Common;
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
        var notification = Assert.Single(hooks.Sent);
        Assert.Equal("composer-1", notification.ComposerId, StringComparer.Ordinal);
        Assert.Equal("audit@example.net", notification.Bcc, StringComparer.Ordinal);
        Assert.Equal(sent.Value!.EmailId, notification.EmailId, StringComparer.Ordinal);
        Assert.Equal(rig.Fake.Client.Session.MailAccountId, notification.JmapAccountId, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TakeoverFailureStopsBeforeTransformOrOrdinarySubmission(bool unknown)
    {
        var hooks = new Hooks { TakeoverError = unknown ? new RawMessageOutcomeUnknown() : new Barbican.Core.Jmap.Errors.ValidationError("Stopped by add-on.") };
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var draft = PipelineRig.NewDraft() with { ComposerId = "composer-1", ComposerRevision = "revision-1" };
        var result = await rig.Sender.SendAsync(draft, new SendOptions(), Ct);
        Assert.False(result.IsOk);
        if (unknown) Assert.IsType<RawMessageOutcomeUnknown>(result.Error);
        Assert.Equal(1, hooks.TakeoverCount);
        Assert.Equal("composer-1", hooks.TakeoverRequest!.ComposerId, StringComparer.Ordinal);
        Assert.Equal("revision-1", hooks.TakeoverRequest.Revision, StringComparer.Ordinal);
        Assert.Equal(0, hooks.TransformCount);
        Assert.Equal(0, hooks.IssueCount);
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(hooks.Sent);
    }

    [Fact]
    public async Task TakeoverReceiptIsAcceptedWithoutTransformOrPlaintextSubmission()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        hooks.TakeoverReceipt = new RawMessageReceipt(PipelineRig.Account, rig.Fake.Client.Session.MailAccountId, "taken-email", "taken-submission", null);
        var draft = PipelineRig.NewDraft() with { ComposerId = "composer-1", ComposerRevision = "revision-1" };
        var result = await rig.Sender.SendAsync(draft, new SendOptions(), Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal("taken-email", result.Value!.EmailId, StringComparer.Ordinal);
        Assert.Equal("taken-submission", result.Value.SubmissionId, StringComparer.Ordinal);
        Assert.Equal(1, hooks.TakeoverCount);
        Assert.Equal(0, hooks.TransformCount);
        Assert.Equal(0, hooks.IssueCount);
        Assert.Empty(rig.Api.Submissions);
        Assert.Equal("taken-submission", Assert.Single(hooks.Sent).SubmissionId, StringComparer.Ordinal);
    }

    [Fact]
    public async Task NoTakeoverRunsTransformAndOrdinarySubmission()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var draft = PipelineRig.NewDraft() with { ComposerId = "composer-1", ComposerRevision = "revision-1" };
        var result = await rig.Sender.SendAsync(draft, new SendOptions(), Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal(1, hooks.TakeoverCount);
        Assert.Equal(1, hooks.TransformCount);
        Assert.Equal(1, hooks.IssueCount);
        Assert.Single(rig.Api.Submissions);
        Assert.Equal("audit@example.net", hooks.Checks[1].Bcc, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ChangedRecipientsMustPassSecondCheck()
    {
        var hooks = new Hooks { RejectSecond = true };
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions(), Ct);
        Assert.False(sent.IsOk);
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(hooks.Sent);
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
    public async Task UndoHoldNotifiesOnceOnSendNowAndNeverOnUndo()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var first = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(first.IsOk, first.Error?.Message);
        Assert.Empty(hooks.Sent);
        Assert.True((await rig.Sender.SendNowAsync(first.Value!, Ct)).IsOk);
        var released = Assert.Single(hooks.Sent);
        var actualSubmission = Assert.Single(rig.Api.SubmissionStates).Id;
        Assert.Equal(actualSubmission, released.SubmissionId, StringComparer.Ordinal);
        Assert.NotEqual(first.Value!.SubmissionId, released.SubmissionId, StringComparer.Ordinal);
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Single(hooks.Sent);
        var second = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(second.IsOk, second.Error?.Message);
        Assert.True((await rig.Sender.UndoAsync(second.Value!, Ct)).IsOk);
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Single(hooks.Sent);
    }

    [Fact]
    public async Task NaturalExpiryNotifiesOnceAndLaterSendNowCannotDuplicateIt()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        rig.Clock.Advance(TimeSpan.FromSeconds(19));
        Assert.Empty(hooks.Sent);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var notification = Assert.Single(hooks.Sent);
        Assert.Equal(sent.Value!.SubmissionId, notification.SubmissionId, StringComparer.Ordinal);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        _ = await rig.Sender.SendNowAsync(sent.Value, Ct);
        Assert.Single(hooks.Sent);
    }

    [Fact]
    public async Task SameSubmissionIdInDifferentAccountsHasIndependentExpiry()
    {
        var hooks = new Hooks();
        var first = await PipelineRig.StartAsync();
        var second = await PipelineRig.StartAsync();
        var accounts = new TwoAccounts(first.Accounts.SessionFor(PipelineRig.Account)!, second.Accounts.SessionFor(PipelineRig.Account)!);
        using var sender = new MailSender(accounts, new IdentityService(accounts), first.Seeder, first.Hooks, first.Codec, first.Clock, hooks);
        var one = await sender.SendAsync(PipelineRig.NewDraft() with { AccountId = "first" }, new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(one.IsOk, one.Error?.Message);
        first.Clock.Advance(TimeSpan.FromSeconds(5));
        var two = await sender.SendAsync(PipelineRig.NewDraft() with { AccountId = "second" }, new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(two.IsOk, two.Error?.Message);
        Assert.Equal(one.Value!.SubmissionId, two.Value!.SubmissionId, StringComparer.Ordinal);
        first.Clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal("first", Assert.Single(hooks.Sent).AccountId, StringComparer.Ordinal);
        first.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, hooks.Sent.Count);
        Assert.Equal("second", hooks.Sent[1].AccountId, StringComparer.Ordinal);
        first.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, hooks.Sent.Count);
    }

    [Fact]
    public async Task ExplicitScheduledSendNotifiesImmediately()
    {
        var hooks = new Hooks();
        var rig = await PipelineRig.StartAsync(addOns: hooks);
        var at = rig.Clock.GetUtcNow().AddHours(1);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { ScheduledAt = at }, Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        Assert.Equal(at, Assert.Single(hooks.Sent).ScheduledFor);
    }

    [Fact]
    public void KeywordValidationIsBoundedAndCaseInsensitive()
    {
        var values = OutgoingKeywords.Clean(["ONE", "one", "$seen", "$flagged", "$label:", "$label:x", "bad space", "bad/ok", "bad]", .. Enumerable.Range(0, 20).Select(i => "k" + i)]);
        Assert.Equal(10, values.Count);
        Assert.Equal(["ONE", "$flagged", "$label:x", "bad/ok"], values.Take(4), StringComparer.Ordinal);
        Assert.DoesNotContain("$seen", values, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TwoAccounts(IAccountSession first, IAccountSession second) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];
        public AccountInfo? Active => null;
        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
        public IAccountSession? SessionFor(string key) => key switch { "first" => first, "second" => second, _ => null };
        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SwitchToAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Hooks : IOutgoingAddOnHooks, IOutgoingAddOnSend
    {
        public List<OutgoingEmailPayload> Checks { get; } = [];
        public List<EmailSentPayload> Sent { get; } = [];
        public void NotifySent(EmailSentPayload payload) => Sent.Add(payload);
        public IAccountSession? Session { get; private set; }
        public int StartCount { get; private set; }
        public int IssueCount { get; private set; }
        public int TakeoverCount { get; private set; }
        public int TransformCount { get; private set; }
        public ComposerTakeoverRequest? TakeoverRequest { get; private set; }
        public RawMessageReceipt? TakeoverReceipt { get; set; }
        public Barbican.Core.Jmap.Errors.JmapError? TakeoverError { get; init; }
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
        public Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct)
        {
            TransformCount++;
            return Task.FromResult(outgoing with { AccountId = "forged", IdentityId = "forged", ComposerId = "forged", Bcc = "audit@example.net", Keywords = ["case-1", "$flagged", "$seen", "$answered"] });
        }
        public Task<Result<RawMessageReceipt?>> TakeOverAsync(ComposerTakeoverRequest request, CancellationToken ct)
        {
            TakeoverCount++;
            TakeoverRequest = request;
            return Task.FromResult(TakeoverError is { } error ? Result<RawMessageReceipt?>.Fail(error) : Result<RawMessageReceipt?>.Ok(TakeoverReceipt));
        }
        public Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct)
        {
            IssueCount++;
            return Task.FromResult(!DenyIssue && issue());
        }
    }
}
