#pragma warning disable MA0002, MA0006, VSTHRD200 // asserts compare strings ordinally by nature; Next waits for a dialog
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Tests.Compose;

/// <summary>The composer's rules: the leave guard matrix (blank, only the quote, edited, saved, sending), autosave, From and sub-address, Cc and Bcc, attachments and the send flow (I-234..I-260).</summary>
public sealed class ComposerViewModelTests
{
    private static async Task<string> Next(ComposerViewModel c)
    {
        // wait until the composer shows a dialog
        for (var i = 0; i < 200 && c.Dialog is null; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        return c.Dialog?.Id ?? "none";
    }

    // ---- the leave guard -------------------------------------------------------------------------------

    [Fact]
    public async Task A_blank_composer_closes_without_asking_and_leaves_no_draft()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        Assert.True(await c.RequestCloseAsync());
        Assert.Null(c.Dialog);
        Assert.Empty(h.Drafts.Saved);
        Assert.Empty(h.Drafts.Discarded);
        Assert.True(c.IsClosed);
    }

    [Fact]
    public async Task A_blank_composer_whose_autosave_made_a_draft_removes_it_on_close()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "x";
        h.Quiet(c);
        await c.WaitForSaveAsync();
        Assert.Single(h.Drafts.Saved);
        c.Subject = string.Empty;
        Assert.True(await c.RequestCloseAsync());
        Assert.Equal(["d1"], h.Drafts.Discarded);
    }

    [Fact]
    public async Task A_reply_that_holds_only_its_quote_closes_silently()
    {
        var h = new ComposerHarness();
        var c = h.Open(ComposerHarness.Seed("On Monday Ann wrote:\n> hello", ComposeKind.Reply, to: "ann@x.example", subject: "Re: hi"));
        Assert.True(await c.RequestCloseAsync());
        Assert.Empty(h.Drafts.Saved);
    }

    [Fact]
    public async Task Edited_content_asks_and_Cancel_keeps_the_composer_open()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        var closing = c.RequestCloseAsync();
        Assert.Equal("leave", await Next(c));
        Assert.Equal(["save", "discard", "cancel"], c.Dialog!.Choices.Select(x => x.Id));
        c.AnswerDialog("cancel");
        Assert.False(await closing);
        Assert.False(c.IsClosed);
        Assert.Null(c.Dialog);
    }

    [Fact]
    public async Task Escape_on_the_question_is_a_Cancel()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        var closing = c.RequestCloseAsync();
        _ = await Next(c);
        c.AnswerDialog(null);
        Assert.False(await closing);
    }

    [Fact]
    public async Task Discard_in_the_question_closes_and_deletes_the_draft_the_composer_made()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        h.Quiet(c);
        await c.WaitForSaveAsync();
        c.Subject = "Plans 2";
        var closing = c.RequestCloseAsync();
        _ = await Next(c);
        c.AnswerDialog("discard");
        Assert.True(await closing);
        Assert.Equal(["d1"], h.Drafts.Discarded);
    }

    [Fact]
    public async Task Save_draft_in_the_question_saves_then_closes()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        var closed = default(ComposerClosedEventArgs);
        c.Closed += (_, e) => closed = e;
        c.Subject = "Plans";
        var closing = c.RequestCloseAsync();
        _ = await Next(c);
        c.AnswerDialog("save");
        Assert.True(await closing);
        Assert.Single(h.Drafts.Saved);
        Assert.Equal("d1", closed!.DraftId);
        Assert.False(closed.Sent);
    }

    [Fact]
    public async Task A_failed_save_in_the_question_keeps_the_composer_open()
    {
        var h = new ComposerHarness();
        h.Drafts.Fail = true;
        var c = h.Open();
        c.Subject = "Plans";
        var closing = c.RequestCloseAsync();
        _ = await Next(c);
        c.AnswerDialog("save");
        Assert.False(await closing);
        Assert.False(c.IsClosed);
        Assert.Contains(h.Toasts.Shown, t => t.IsError);
    }

    [Fact]
    public async Task Content_the_server_already_has_closes_without_asking()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        h.Quiet(c);
        await c.WaitForSaveAsync();
        Assert.True(await c.RequestCloseAsync());
        Assert.Empty(h.Drafts.Discarded);
        Assert.Equal("Draft saved 10:00", c.Status);
    }

    [Fact]
    public async Task A_reopened_draft_that_was_not_touched_closes_and_stays_on_the_server()
    {
        var h = new ComposerHarness();
        var c = h.Open(ComposerHarness.Seed("kept", subject: "Old", draftId: "d9", saved: true));
        Assert.True(await c.RequestCloseAsync());
        Assert.Empty(h.Drafts.Discarded);
    }

    [Fact]
    public async Task Discard_asks_for_confirmation_unless_the_composer_is_blank()
    {
        var h = new ComposerHarness();
        var blank = h.Open();
        Assert.True(await blank.DiscardAsync());
        var c = h.Open();
        c.Subject = "Plans";
        var discarding = c.DiscardAsync();
        Assert.Equal("discard", await Next(c));
        c.AnswerDialog("cancel");
        Assert.False(await discarding);
        Assert.False(c.IsClosed);
    }

    // ---- autosave --------------------------------------------------------------------------------------

    [Fact]
    public void Autosave_waits_for_two_quiet_seconds_and_saves_once()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        c.Tick();
        h.Time.Advance(TimeSpan.FromMilliseconds(1000));
        c.Tick();
        Assert.Empty(h.Drafts.Saved);
        h.Time.Advance(TimeSpan.FromMilliseconds(1200));
        c.Tick();
        Assert.Single(h.Drafts.Saved);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        c.Tick();
        Assert.Single(h.Drafts.Saved);
    }

    [Fact]
    public void A_fresh_composer_never_autosaves()
    {
        var h = new ComposerHarness();
        var c = h.Open(ComposerHarness.Seed("quote", ComposeKind.Reply, to: "a@x.example"));
        h.Quiet(c);
        Assert.Empty(h.Drafts.Saved);
    }

    [Fact]
    public void Autosave_does_not_turn_half_typed_recipient_text_into_a_chip()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "x";
        c.To.Edited("an");
        h.Quiet(c);
        Assert.Equal(string.Empty, c.To.Text);
        Assert.Equal("an", c.To.Input);
    }

    [Fact]
    public void The_content_key_changes_with_the_fields_and_not_without_them()
    {
        var c = new ComposerHarness().Open();
        var a = c.ComputeKey();
        Assert.Equal(a, c.ComputeKey());
        c.Subject = "z";
        Assert.NotEqual(a, c.ComputeKey());
    }

    [Fact]
    public async Task Draft_change_hook_hears_the_text_once_per_change()
    {
        var hooks = new HookBus();
        var heard = new List<DraftViewPayload>();
        using var sub = hooks.Observe<DraftViewPayload>(HookNames.OnDraftChange, (p, _) =>
        {
            heard.Add(p);
            return ValueTask.CompletedTask;
        });
        var h = new ComposerHarness(hooks: hooks);
        var c = h.Open();
        c.Subject = "Plans";
        c.Tick();
        c.Tick();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(heard);
        Assert.Equal("Plans", heard[0].Subject);
    }

    // ---- From, Cc and Bcc ------------------------------------------------------------------------------

    [Fact]
    public void The_first_identity_is_the_default_and_the_seeded_one_wins()
    {
        var h = new ComposerHarness();
        Assert.Equal("i1", h.Open().SelectedIdentityId);
        var seeded = h.Open(new ComposeSeed { AccountId = "acc", Body = Barbican.Core.Editor.Doc.FromText(string.Empty), IdentityId = "i2" });
        Assert.Equal("i2", seeded.SelectedIdentityId);
        Assert.Equal("Work <me@work.example>", seeded.FromText);
    }

    [Fact]
    public void A_sub_address_tag_changes_the_From_address_and_a_bad_tag_is_refused()
    {
        var c = new ComposerHarness().Open();
        Assert.True(c.CanTag);
        c.SubAddressTag = "shop";
        Assert.Equal("me+shop@home.example", c.SubAddressPreview);
        Assert.Equal(new Barbican.Core.Jmap.Submission.FromOverride("Me", "me+shop@home.example"), c.BuildDraft().FromOverride);
        c.SubAddressTag = "no spaces!";
        Assert.NotNull(c.SubAddressError);
        Assert.Null(c.BuildDraft().FromOverride);
    }

    [Fact]
    public void An_edited_sender_wins_over_the_identity_and_the_tag()
    {
        var c = new ComposerHarness().Open();
        c.SubAddressTag = "shop";
        c.FromOverrideEnabled = true;
        c.FromOverrideName = "Boss";
        c.FromOverrideEmail = "boss@home.example";
        Assert.Equal(new Barbican.Core.Jmap.Submission.FromOverride("Boss", "boss@home.example"), c.BuildDraft().FromOverride);
        Assert.Equal("Boss <boss@home.example>", c.FromText);
    }

    [Fact]
    public void Cc_and_Bcc_show_when_seeded_and_on_Ctrl_Shift_C_and_B()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        Assert.False(c.ShowCc);
        Assert.False(c.ShowBcc);
        Assert.True(c.HandleChord("C", control: true, shift: true, alt: false));
        Assert.True(c.ShowCc);
        Assert.True(c.HandleChord("b", true, true, false));
        Assert.True(c.ShowBcc);
        Assert.False(c.HandleChord("c", false, true, false));
        var seeded = h.Open(new ComposeSeed { AccountId = "acc", Body = Barbican.Core.Editor.Doc.FromText(string.Empty), Cc = "c@x.example" });
        Assert.True(seeded.ShowCc);
        Assert.False(seeded.ShowBcc);
    }

    [Fact]
    public void The_recipients_go_into_the_draft_as_message_text_with_groups_expanded_and_domains_in_ASCII()
    {
        var c = new ComposerHarness().Open();
        c.To.Seed("Team: a@x.example, b@bücher.example;");
        var draft = c.BuildDraft();
        Assert.Equal("a@x.example, b@xn--bcher-kva.example", draft.To);
    }

    [Fact]
    public async Task The_recipient_hook_may_change_the_list()
    {
        var hooks = new HookBus();
        using var sub = hooks.Transform<RecipientChipsPayload>(HookNames.OnRecipientChipsChange, (p, _) =>
            ValueTask.FromResult(p with { Recipients = [.. p.Recipients, Recipient.Addr("", "auto@x.example")] }));
        var c = new ComposerHarness(hooks: hooks).Open();
        c.To.Edited("a@x.example");
        _ = c.To.Key("Enter", false);
        for (var i = 0; i < 100 && !c.To.Text.Contains("auto@", StringComparison.Ordinal); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal("a@x.example, auto@x.example", c.To.Text);
    }

    // ---- attachments -----------------------------------------------------------------------------------

    [Fact]
    public async Task An_attachment_uploads_into_a_chip_with_a_blob()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        await c.AddFilesAsync([new AttachmentUpload("a.txt", "text/plain", new byte[] { 1, 2, 3 })]);
        var item = Assert.Single(c.Attachments);
        Assert.Equal(AttachmentState.Done, item.State);
        Assert.Equal("b-a.txt", item.Attachment.BlobId);
        Assert.Equal("3 B", item.SizeText);
        Assert.Single(c.BuildDraft().Attachments);
    }

    [Fact]
    public async Task A_refused_upload_removes_the_chip_and_says_why()
    {
        var h = new ComposerHarness();
        h.Uploader.Hold = new TaskCompletionSource<Barbican.Core.Common.Result<DraftAttachment>>();
        var c = h.Open();
        var adding = c.AddFilesAsync([new AttachmentUpload("big.bin", "application/octet-stream", new byte[10])]);
        Assert.True(c.HasPendingUploads);
        h.Uploader.Hold.SetResult(Barbican.Core.Common.Result<DraftAttachment>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("File too large")));
        await adding;
        Assert.Empty(c.Attachments);
        Assert.Equal("File too large", h.Toasts.Shown.Single().Message);
    }

    [Fact]
    public async Task Cancelling_an_upload_removes_its_chip()
    {
        var h = new ComposerHarness();
        h.Uploader.Hold = new TaskCompletionSource<Barbican.Core.Common.Result<DraftAttachment>>();
        var c = h.Open();
        var adding = c.AddFilesAsync([new AttachmentUpload("a.bin", "application/octet-stream", new byte[10])]);
        c.RemoveAttachment(c.Attachments[0]);
        await adding;
        Assert.Empty(c.Attachments);
        Assert.False(c.HasPendingUploads);
    }

    // ---- the send flow ---------------------------------------------------------------------------------

    private static ComposerViewModel Ready(ComposerHarness h)
    {
        var c = h.Open();
        c.To.Seed("ann@x.example");
        c.Subject = "Plans";
        return c;
    }

    [Fact]
    public async Task A_send_waits_for_uploads_and_says_so()
    {
        var h = new ComposerHarness();
        h.Uploader.Hold = new TaskCompletionSource<Barbican.Core.Common.Result<DraftAttachment>>();
        var c = Ready(h);
        _ = c.AddFilesAsync([new AttachmentUpload("a.bin", "application/octet-stream", new byte[10])]);
        var sending = c.SendAsync();
        Assert.Equal("pending-uploads", await Next(c));
        c.AnswerDialog("ok");
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task An_address_that_is_none_stops_the_send_and_is_named()
    {
        var h = new ComposerHarness();
        var c = Ready(h);
        c.To.Seed("ann@x.example, nope");
        Assert.False(await c.SendAsync());
        Assert.Contains("nope", h.Toasts.Shown.Single().Message, StringComparison.Ordinal);
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task A_send_needs_a_recipient()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        Assert.False(await c.SendAsync());
        Assert.Equal("Add at least one recipient.", h.Toasts.Shown.Single().Message);
    }

    [Fact]
    public async Task Every_warning_is_listed_in_one_dialog_and_Send_anyway_sends()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        c.FallbackBody = "see the attached file {{ name }}";
        var sending = c.SendAsync();
        Assert.Equal("warnings", await Next(c));
        Assert.Equal(3, c.Dialog!.Lines.Count);
        Assert.Contains(c.Dialog.Lines, l => l.Contains("subject", StringComparison.Ordinal));
        Assert.Contains(c.Dialog.Lines, l => l.Contains("Placeholders", StringComparison.Ordinal));
        Assert.Contains(c.Dialog.Lines, l => l.Contains("attachment", StringComparison.Ordinal));
        c.AnswerDialog("send");
        Assert.True(await sending);
        Assert.Single(h.Sender.Sent);
    }

    [Fact]
    public async Task Go_back_on_the_warnings_sends_nothing()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        var sending = c.SendAsync();
        _ = await Next(c);
        c.AnswerDialog("cancel");
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
        Assert.False(c.IsClosed);
    }

    [Fact]
    public async Task External_recipients_are_confirmed_only_when_the_policy_asks()
    {
        var h = new ComposerHarness();
        var c = Ready(h);
        Assert.True(await c.SendAsync());
        var strict = new ComposerHarness();
        var s = new ComposerViewModel(strict.Context with { ConfirmExternalRecipients = true }, ComposerHarness.Seed(), ComposerHarness.Identities());
        s.To.Seed("ann@x.example");
        s.Subject = "Plans";
        var sending = s.SendAsync();
        Assert.Equal("warnings", await Next(s));
        Assert.Contains(s.Dialog!.Lines, l => l.Contains("ann@x.example", StringComparison.Ordinal));
        s.AnswerDialog("cancel");
        Assert.False(await sending);
    }

    [Fact]
    public async Task A_sent_message_closes_the_composer_as_sent_with_the_undo_window()
    {
        var h = new ComposerHarness(new ComposeSettings { SendDelaySeconds = 10 });
        h.Sender.CanUndo = true;
        var c = Ready(h);
        var closed = default(ComposerClosedEventArgs);
        c.Closed += (_, e) => closed = e;
        Assert.True(await c.SendAsync());
        var (draft, options) = Assert.Single(h.Sender.Sent);
        Assert.Equal(10, options.UndoSeconds);
        Assert.Null(options.ScheduledAt);
        Assert.Equal("ann@x.example", draft.To);
        Assert.True(closed!.Sent);
        var toast = h.Toasts.Shown.Single();
        Assert.Equal("Undo", toast.ActionLabel);
    }

    [Fact]
    public async Task A_failed_send_keeps_the_composer_and_its_text()
    {
        var h = new ComposerHarness();
        h.Sender.Fail = true;
        var c = Ready(h);
        Assert.False(await c.SendAsync());
        Assert.False(c.IsClosed);
        Assert.False(c.IsSending);
        Assert.Equal("Not sent", c.Status);
        Assert.Contains(h.Toasts.Shown, t => t.IsError && t.Message.Contains("rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_on_the_toast_reopens_the_message()
    {
        var h = new ComposerHarness();
        h.Sender.CanUndo = true;
        var reopened = new List<ComposeSeed>();
        var ctx = h.Context with { Reopen = (s, _) => { reopened.Add(s); return Task.CompletedTask; } };
        var c = new ComposerViewModel(ctx, ComposerHarness.Seed(subject: "Plans", to: "ann@x.example"), ComposerHarness.Identities());
        Assert.True(await c.SendAsync());
        await h.Toasts.Shown.Single().Action!(CancellationToken.None);
        var body = reopened.Single().Body;
        Assert.Equal("back", body.TextRange(new Barbican.Core.Editor.Pos(0, 0), body.End));
    }

    [Fact]
    public async Task The_schedule_dialog_takes_a_preset_and_sends_with_that_time()
    {
        var h = new ComposerHarness();
        var c = Ready(h);
        var scheduling = c.OpenScheduleAsync();
        Assert.NotNull(c.Schedule);
        var state = c.Schedule!;
        Assert.NotEmpty(state.Presets);
        state.Choose(0);
        Assert.True(await scheduling);
        var (_, options) = Assert.Single(h.Sender.Sent);
        Assert.Equal(state.Presets[0].At, options.ScheduledAt);
        Assert.Equal(0, options.UndoSeconds);
    }

    [Fact]
    public async Task The_schedule_dialog_refuses_a_past_time_and_a_blank_one_with_Bulwarks_messages()
    {
        var h = new ComposerHarness();
        h.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var state = new Barbican.Presentation.Compose.Pipeline.ScheduleDialogViewModel(Barbican.Presentation.Compose.Pipeline.ScheduleMode.Compose, h.Time, 7 * 24 * 3600, false)
        {
            Date = string.Empty,
            Time = string.Empty,
        };
        _ = state.ChooseTyped();
        Assert.Equal("Choose a date and time.", state.Error);
        state.Date = "2026-11-17";
        state.Time = "08:00";
        _ = state.ChooseTyped();
        Assert.Equal("Choose a future date and time.", state.Error);
        state.Date = "2026-12-31";
        _ = state.ChooseTyped();
        Assert.Equal("This time is later than the server allows.", state.Error);
        state.Date = "2026-11-20";
        _ = state.ChooseTyped();
        Assert.Empty(state.Error);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), await state.Completion);
        Assert.Equal("Reschedule", new Barbican.Presentation.Compose.Pipeline.ScheduleDialogViewModel(Barbican.Presentation.Compose.Pipeline.ScheduleMode.Reschedule, h.Time, 100, false).Title);
    }

    // ---- templates and mentions ------------------------------------------------------------------------

    [Fact]
    public async Task The_template_picker_filters_inserts_saves_and_deletes()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        await c.OpenTemplatesAsync();
        var picker = c.Templates!;
        Assert.Equal(2, picker.Items.Count);
        picker.Search = "sales";
        Assert.Equal("Welcome", Assert.Single(picker.Items).Name);
        picker.Search = "invoice";
        await picker.InsertAsync(picker.Items[0]);
        Assert.Null(c.Templates);
        Assert.Equal("Your invoice", c.Subject);
        Assert.Contains("See attached", c.FallbackBody, StringComparison.Ordinal);

        await c.OpenTemplatesAsync();
        c.Templates!.SaveName = "Mine";
        await c.Templates.SaveAsync();
        Assert.Equal(3, c.Templates.Items.Count);
        await c.Templates.DeleteAsync(c.Templates.Items.First(t => t.Name == "Mine"));
        Assert.Equal(2, c.Templates.Items.Count);
        c.CloseTemplates();
        Assert.Null(c.Templates);
    }

    [Fact]
    public void Mentions_list_the_recipients_first_and_a_pick_adds_the_person_to_the_setting_field()
    {
        var c = new ComposerHarness().Open();
        c.SetSources(new SuggestionSources { Contacts = [("Zed Zoe", "zed@x.example")] });
        c.To.Seed("Ann <ann@x.example>");
        var cands = c.Candidates(string.Empty);
        Assert.Equal("ann@x.example", cands[0].Email);
        Assert.Contains(cands, m => m.Email == "zed@x.example");
        c.Picked(new Barbican.Presentation.Editor.MentionCandidate("Zed Zoe", "zed@x.example"));
        Assert.Contains("zed@x.example", c.To.Text, StringComparison.Ordinal);
        var before = c.To.Text;
        c.Picked(new Barbican.Presentation.Editor.MentionCandidate("Zed Zoe", "zed@x.example"));
        Assert.Equal(before, c.To.Text);
    }
}
