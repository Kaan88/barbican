#pragma warning disable MA0002, MA0006, VSTHRD200 // asserts compare strings ordinally by nature
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Tests.Accounts;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Compose;

/// <summary>The composer service (open, hooks, the replace question, autosave timer, close hook), the mailto router, the compose commands and the draft banner.</summary>
public sealed class ComposerServiceTests
{
    private static readonly AccountInfo Ana = FakeAccounts.Info("ana");
    private static readonly AccountInfo Ben = FakeAccounts.Info("ben");

    private sealed class Seeder : IComposeSeeder
    {
        public List<ComposeRequest> Requests { get; } = [];

        public bool Fail { get; set; }

        public TaskCompletionSource<Result<ComposeSeed>>? Waiting { get; set; }

        public Task<Result<ComposeSeed>> SeedAsync(ComposeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Waiting is { } waiting) return waiting.Task.WaitAsync(ct);
            if (Fail)
            {
                return Task.FromResult(Result<ComposeSeed>.Fail(new ValidationError("no such message")));
            }

            var p = request.Prefill;
            return Task.FromResult(Result<ComposeSeed>.Ok(new ComposeSeed
            {
                AccountId = request.AccountId,
                Kind = request.Kind,
                To = p?.To ?? string.Empty,
                Subject = p?.Subject ?? string.Empty,
                Body = Barbican.Core.Editor.Doc.FromText(p?.Body ?? string.Empty),
                SourceEmailId = request.SourceEmailId,
            }));
        }
    }

    private sealed class Identities : IIdentityService
    {
        public Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<ComposeIdentity>>.Ok(ComposerHarness.Identities()));
    }

    private sealed class World
    {
        public World(HookBus? hooks = null, string prefs = "{}")
        {
            Harness = new ComposerHarness(hooks: hooks);
            Hooks = hooks ?? new HookBus();
            var services = new ComposerServices(Seeder, new Identities(), Harness.Drafts, Harness.Uploader, Harness.Sender, new ComposerHarness.NoEditor(), new Barbican.Core.Compose.Recipients.EmptyRecipientSources(), Hooks, Harness.Time, new TestPrefs(prefs), new InlineDispatcher())
            {
                Toasts = Harness.Toasts,
                MessageOpen = () => MessageOpen,
            };
            Service = new ComposerService(services);
        }

        public ComposerHarness Harness { get; }

        public HookBus Hooks { get; }

        public Seeder Seeder { get; } = new();

        public ComposerService Service { get; }

        public bool MessageOpen { get; set; }
    }

    [Theory]
    [InlineData("send", true)]
    [InlineData("cancel", false)]
    public async Task Inline_reply_uses_send_confirmation_without_opening_an_editor(string answer, bool expected)
    {
        var w = new World(prefs: """{"sendConfirmation":true}""");
        var request = new ComposeRequest("acc", ComposeKind.Reply)
        {
            InlineReply = true, SourceEmailId = "m1", DraftText = "Hello",
            Prefill = new ComposePrefill { To = "to@example.net", Subject = "Re: Hello", Body = "Hello" },
        };
        var shown = 0;
        var sent = await w.Service.SendInlineReplyAsync(request, (dialog, _) =>
        {
            Assert.Equal("confirm", dialog.Id);
            shown++;
            return Task.FromResult<string?>(answer);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, shown);
        Assert.Equal(expected, sent);
        Assert.Equal(expected ? 1 : 0, w.Harness.Sender.Sent.Count);
        Assert.False(w.Service.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_open_guards_the_composer_created_by_the_first_request(bool fromSeed)
    {
        var w = new World();
        var ct = TestContext.Current.CancellationToken;
        var waiting = new TaskCompletionSource<Result<ComposeSeed>>(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Seeder.Waiting = waiting;
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Service.OpenChanged += (_, _) =>
        {
            if (w.Service.Current is not { } opened) return;
            opened.Subject = "Edited before replacement";
            opened.PropertyChanged += (_, _) =>
            {
                if (opened.Dialog is not null) confirmation.TrySetResult();
            };
        };
        var first = w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), ct);
        var seed = new ComposeSeed { AccountId = "acc", Kind = ComposeKind.New, Body = Barbican.Core.Editor.Doc.FromText(string.Empty) };
        var replacement = fromSeed ? w.Service.OpenSeedAsync(seed, ct)
            : w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), ct);
        Assert.Single(w.Seeder.Requests);
        waiting.SetResult(Result<ComposeSeed>.Ok(seed));
        await first;
        var original = Assert.IsType<ComposerViewModel>(w.Service.Current);
        await confirmation.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal("leave", original.Dialog!.Id);
        original.AnswerDialog("cancel");
        await replacement;
        Assert.Same(original, w.Service.Current);
    }

    [Fact]
    public async Task Inline_reply_can_retry_after_failure_and_undo_the_successful_send()
    {
        var w = new World();
        var request = new ComposeRequest("acc", ComposeKind.Reply)
        {
            InlineReply = true, SourceEmailId = "m1", DraftText = "Keep this reply",
            Prefill = new ComposePrefill { To = "to@example.net", Subject = "Re: plans", Body = "Keep this reply" },
        };
        var ct = TestContext.Current.CancellationToken;
        w.Harness.Sender.Fail = true;
        Assert.False(await w.Service.SendInlineReplyAsync(request, (_, _) => Task.FromResult<string?>("send"), ct));
        Assert.False(w.Service.IsOpen);
        w.Harness.Sender.Fail = false;
        w.Harness.Sender.CanUndo = true;
        Assert.True(await w.Service.SendInlineReplyAsync(request, (_, _) => Task.FromResult<string?>("send"), ct));
        var undo = Assert.Single(w.Harness.Toasts.Shown, t => string.Equals(t.ActionLabel, "Undo", StringComparison.Ordinal));
        await undo.Action!(ct);
        Assert.True(w.Service.IsOpen);
        Assert.Equal(2, w.Harness.Sender.Sent.Count);
    }

    [Fact]
    public async Task Opening_a_composer_seeds_it_and_makes_it_current()
    {
        var w = new World();
        var changed = 0;
        w.Service.OpenChanged += (_, _) => changed++;
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        Assert.True(w.Service.IsOpen);
        Assert.Equal("acc", w.Service.Current!.AccountKey);
        Assert.Equal(2, w.Service.Current.Identities.Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task A_seed_that_fails_shows_a_toast_and_opens_nothing()
    {
        var w = new World();
        w.Seeder.Fail = true;
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.Reply) { SourceEmailId = "e1" }, CancellationToken.None);
        Assert.False(w.Service.IsOpen);
        Assert.Contains("no such message", w.Harness.Toasts.Shown.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_plugin_hooks_can_stop_a_composer()
    {
        var hooks = new HookBus();
        using var compose = hooks.Intercept<ComposeOptionsPayload>(HookNames.OnBeforeCompose, (p, _) => ValueTask.FromResult(!string.Equals(p.Subject, "blocked", StringComparison.Ordinal)));
        using var reply = hooks.Intercept<ReplyContextPayload>(HookNames.OnBeforeReply, (_, _) => ValueTask.FromResult(false));
        var w = new World(hooks);
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New) { Prefill = new ComposePrefill { Subject = "blocked" } }, CancellationToken.None);
        Assert.False(w.Service.IsOpen);
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.Reply) { SourceEmailId = "e1" }, CancellationToken.None);
        Assert.False(w.Service.IsOpen);
        Assert.Empty(w.Seeder.Requests);
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.ReplyAll) { SourceEmailId = "e1" }, CancellationToken.None);
        Assert.True(w.Service.IsOpen);
    }

    [Fact]
    public async Task The_open_and_close_hooks_fire()
    {
        var hooks = new HookBus();
        var opened = new List<ComposerContextPayload>();
        var closed = new List<ComposerClosedPayload>();
        using var o = hooks.Observe<ComposerContextPayload>(HookNames.OnComposerOpen, (p, _) =>
        {
            opened.Add(p);
            return ValueTask.CompletedTask;
        });
        using var c = hooks.Observe<ComposerClosedPayload>(HookNames.OnComposerClose, (p, _) =>
        {
            closed.Add(p);
            return ValueTask.CompletedTask;
        });
        var w = new World(hooks);
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        Assert.True(await w.Service.TryCloseAsync(CancellationToken.None));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("New", Assert.Single(opened).Kind);
        Assert.False(Assert.Single(closed).Sent);
        Assert.False(w.Service.IsOpen);
    }

    [Fact]
    public async Task A_second_composer_asks_about_the_first_and_Cancel_keeps_it()
    {
        var w = new World();
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        var first = w.Service.Current!;
        first.Subject = "Plans";
        var second = w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        for (var i = 0; i < 100 && first.Dialog is null; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal("leave", first.Dialog!.Id);
        first.AnswerDialog("cancel");
        await second;
        Assert.Same(first, w.Service.Current);
    }

    [Fact]
    public async Task The_timer_ticks_the_autosave_every_half_second()
    {
        var w = new World();
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        w.Service.Current!.Subject = "Plans";
        for (var i = 0; i < 6; i++)
        {
            w.Harness.Time.Advance(TimeSpan.FromMilliseconds(500));
        }

        await w.Service.Current.WaitForSaveAsync();
        Assert.Single(w.Harness.Drafts.Saved);
    }

    [Fact]
    public async Task A_reply_docks_under_the_open_message_only_when_the_setting_is_on_and_a_message_is_open()
    {
        var w = new World { MessageOpen = true };
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.Reply) { SourceEmailId = "e1" }, CancellationToken.None);
        Assert.True(w.Service.Current!.IsDocked);
        _ = await w.Service.TryCloseAsync(CancellationToken.None);
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        Assert.False(w.Service.Current!.IsDocked);
        _ = await w.Service.TryCloseAsync(CancellationToken.None);
        var off = new World(prefs: """{"replyUnderMessage": false}""") { MessageOpen = true };
        await off.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.Reply) { SourceEmailId = "e1" }, CancellationToken.None);
        Assert.False(off.Service.Current!.IsDocked);
    }

    [Fact]
    public async Task Quitting_saves_unsaved_work_silently()
    {
        var w = new World();
        await w.Service.OpenAsync(new ComposeRequest("acc", ComposeKind.New), CancellationToken.None);
        w.Service.Current!.Subject = "Plans";
        await w.Service.FlushAsync(CancellationToken.None);
        Assert.Single(w.Harness.Drafts.Saved);
        Assert.Empty(w.Harness.Toasts.Shown);
    }

    // ---- mailto ----------------------------------------------------------------------------------------

    private static (MailtoRouter Router, World World, FakeAccounts Accounts) Router(HookBus? hooks = null, params AccountInfo[] accounts)
    {
        var w = new World(hooks);
        var fake = new FakeAccounts();
        if (accounts.Length > 0)
        {
            fake.Set([.. accounts.Select((a, i) => new AccountStatus(a, i == 0, i == 0, true, 0, null))]);
        }

        return (new MailtoRouter(w.Service, fake, w.Hooks, null, w.Harness.Toasts), w, fake);
    }

    [Fact]
    public async Task A_mailto_link_opens_the_composer_filled_in()
    {
        var (router, w, _) = Router(null, Ana);
        Assert.True(await router.HandleAsync("mailto:ann@x.example?subject=Hi%20there&body=Line", CancellationToken.None));
        var seed = Assert.Single(w.Seeder.Requests);
        Assert.Equal(Ana.Key, seed.AccountId);
        Assert.Equal("ann@x.example", seed.Prefill!.To);
        Assert.Equal("Hi there", seed.Prefill.Subject);
        Assert.Equal("Line", seed.Prefill.Body);
        Assert.Equal("ann@x.example", w.Service.Current!.To.Text);
    }

    [Fact]
    public async Task Something_that_is_no_mailto_link_does_nothing()
    {
        var (router, w, _) = Router(null, Ana);
        Assert.False(await router.HandleAsync("http://example.org", CancellationToken.None));
        Assert.Empty(w.Seeder.Requests);
    }

    [Fact]
    public async Task The_intercept_hook_can_stop_a_mailto_link()
    {
        var hooks = new HookBus();
        using var sub = hooks.Intercept<MailtoContextPayload>(HookNames.OnMailtoIntercept, (_, _) => ValueTask.FromResult(false));
        var (router, w, _) = Router(hooks, Ana);
        Assert.False(await router.HandleAsync("mailto:a@x.example", CancellationToken.None));
        Assert.False(w.Service.IsOpen);
    }

    [Fact]
    public async Task Links_wait_for_a_sign_in_and_open_when_an_account_arrives()
    {
        var (router, w, accounts) = Router(null);
        Assert.False(await router.HandleAsync("mailto:a@x.example", CancellationToken.None));
        Assert.False(await router.HandleAsync("mailto:b@x.example?subject=Two", CancellationToken.None));
        Assert.Equal(2, router.Pending.Count);
        Assert.Equal("Sign in to write to a@x.example.", w.Harness.Toasts.Shown[0].Message);
        accounts.Set(new AccountStatus(Ana, true, true, true, 0, null));
        await router.RetryPendingAsync(CancellationToken.None);
        Assert.Empty(router.Pending);
        Assert.Equal(2, w.Seeder.Requests.Count);
        Assert.Equal("Two", w.Service.Current!.Subject);
    }

    [Fact]
    public async Task Only_the_newest_five_waiting_links_are_kept()
    {
        var (router, _, _) = Router(null);
        for (var i = 0; i < 8; i++)
        {
            _ = await router.HandleAsync("mailto:a@x.example?subject=" + i, CancellationToken.None);
        }

        Assert.Equal(5, router.Pending.Count);
        Assert.Equal("3", router.Pending[0].Subject);
    }

    [Fact]
    public async Task With_several_accounts_the_link_asks_which_one_writes_it()
    {
        var w = new World();
        var fake = new FakeAccounts();
        fake.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        var dialogs = new PickingDialogs(Ben.Key);
        var router = new MailtoRouter(w.Service, fake, w.Hooks, dialogs, w.Harness.Toasts);
        Assert.True(await router.HandleAsync("mailto:a@x.example", CancellationToken.None));
        Assert.Equal(Ben.Key, w.Seeder.Requests.Single().AccountId);
        Assert.Equal(Ana.Key, dialogs.Asked!.Preferred);
        Assert.Equal(2, dialogs.Asked.Accounts.Count);
        dialogs.Pick = null;
        _ = await w.Service.TryCloseAsync(CancellationToken.None);
        Assert.False(await router.HandleAsync("mailto:a@x.example", CancellationToken.None));
        Assert.False(w.Service.IsOpen);
    }

    private sealed class PickingDialogs(string? pick) : IDialogService
    {
        public string? Pick { get; set; } = pick;

        public AccountPickRequest? Asked { get; private set; }

        public bool IsOpen => false;

        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
        {
            Asked = request as AccountPickRequest;
            return Task.FromResult((TResult?)(object?)Pick);
        }
    }

    // ---- commands --------------------------------------------------------------------------------------

    private static (IReadOnlyList<IAppCommand> Commands, World World) Commands()
    {
        var w = new World();
        var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(Ana, true, true, true, 0, null));
        return (ComposerCommands.Create(w.Service, new FakeActiveMail { AccountKey = Ana.Key }, accounts), w);
    }

    private static IAppCommand Get(IReadOnlyList<IAppCommand> list, string id) => list.Single(c => c.Id == id);

    private static CommandContext Ctx(string? argument = null, params MailTarget[] targets) => new(CommandSurface.Keyboard, targets, null, argument, Ana.Key);

    private static MailTarget Target() => new(Ana.Key, "jmap-acc", "e42", "t1", "row1", new Dictionary<string, bool>(), Keywords.None);

    [Fact]
    public async Task The_compose_commands_cover_every_id_and_open_the_right_kind()
    {
        var (commands, w) = Commands();
        Assert.Equal(ComposerCommands.Count, commands.Count);
        _ = await Get(commands, CommandIds.Compose).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal(ComposeKind.New, w.Seeder.Requests[^1].Kind);
        _ = await w.Service.TryCloseAsync(CancellationToken.None);
        foreach (var (id, kind) in new[] { (CommandIds.Reply, ComposeKind.Reply), (CommandIds.ReplyAll, ComposeKind.ReplyAll), (CommandIds.Forward, ComposeKind.Forward), (CommandIds.ForwardAsAttachment, ComposeKind.ForwardAsAttachment) })
        {
            var ctx = Ctx(null, Target());
            Assert.True(Get(commands, id).CanExecute(ctx));
            _ = await Get(commands, id).ExecuteAsync(ctx, CancellationToken.None);
            Assert.Equal(kind, w.Seeder.Requests[^1].Kind);
            Assert.Equal("e42", w.Seeder.Requests[^1].SourceEmailId);
            _ = await w.Service.TryCloseAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Reply_needs_exactly_one_message()
    {
        var (commands, _) = Commands();
        Assert.False(Get(commands, CommandIds.Reply).CanExecute(Ctx()));
        Assert.False(Get(commands, CommandIds.Forward).CanExecute(Ctx(null, Target(), Target())));
    }

    [Fact]
    public async Task New_message_to_fills_the_address_and_Cc_Bcc_commands_need_an_open_composer()
    {
        var (commands, w) = Commands();
        Assert.False(Get(commands, CommandIds.ComposeShowCc).CanExecute(Ctx()));
        Assert.False(Get(commands, CommandIds.NewMessageTo).CanExecute(Ctx()));
        _ = await Get(commands, CommandIds.NewMessageTo).ExecuteAsync(Ctx("ann@x.example"), CancellationToken.None);
        Assert.Equal("ann@x.example", w.Service.Current!.To.Text);
        Assert.True(Get(commands, CommandIds.ComposeShowCc).CanExecute(Ctx()));
        _ = await Get(commands, CommandIds.ComposeShowCc).ExecuteAsync(Ctx(), CancellationToken.None);
        _ = await Get(commands, CommandIds.ComposeShowBcc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.True(w.Service.Current.ShowCc);
        Assert.True(w.Service.Current.ShowBcc);
    }

    // ---- the draft banner ------------------------------------------------------------------------------

    [Fact]
    public async Task A_draft_shows_the_banner_and_Edit_opens_it_in_the_composer()
    {
        var w = new World();
        var source = new DraftBannerSource(w.Service, w.Harness.Drafts, new FakeActiveMail { AccountKey = Ana.Key }, w.Harness.Toasts);
        var draft = new ReaderSnapshot { EmailId = "e7", Body = new EmailBody { Id = "e7", Keywords = Keywords.Of("$draft") } };
        var banners = new List<ReaderBanner>();
        await foreach (var b in source.BannersForAsync(draft, CancellationToken.None))
        {
            banners.Add(b);
        }

        Assert.Equal(["draft.edit", "draft.discard"], banners.Single().Actions.Select(a => a.ActionId));
        Assert.True(source.Owns("draft.edit"));
        Assert.False(source.Owns("receipt.send"));
        _ = await source.RunAsync(draft, "draft.edit", CancellationToken.None);
        Assert.Equal(ComposeKind.EditDraft, w.Seeder.Requests.Single().Kind);
        Assert.Equal("e7", w.Seeder.Requests.Single().SourceEmailId);
    }

    [Fact]
    public async Task Discard_removes_the_draft_and_a_sent_message_has_no_draft_banner()
    {
        var w = new World();
        var source = new DraftBannerSource(w.Service, w.Harness.Drafts, new FakeActiveMail { AccountKey = Ana.Key }, w.Harness.Toasts);
        var draft = new ReaderSnapshot { EmailId = "e7", Body = new EmailBody { Id = "e7", Keywords = Keywords.Of("$draft") } };
        Assert.Equal("Draft discarded.", await source.RunAsync(draft, "draft.discard", CancellationToken.None));
        Assert.Equal(["e7"], w.Harness.Drafts.Discarded);
        var sent = new ReaderSnapshot { EmailId = "e8", Body = new EmailBody { Id = "e8" } };
        await foreach (var b in source.BannersForAsync(sent, CancellationToken.None))
        {
            Assert.Fail("no banner expected: " + b.Id);
        }
    }
}
