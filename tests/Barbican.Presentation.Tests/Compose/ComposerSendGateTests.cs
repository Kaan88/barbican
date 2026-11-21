#pragma warning disable MA0002, MA0006, VSTHRD200 // asserts compare strings ordinally by nature; Next waits for a dialog
using Barbican.Core.Compose;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Tests.Compose;

/// <summary>CP-07: the questions the composer asks before it sends, schedules, closes or discards (Bulwark send-warnings-dialog.tsx, external-recipient-confirm.tsx, schedule-send-dialog.tsx, the close dialog of
/// email-composer.tsx:6854-6879) are shown by the shared dialog host. The host returns the id of a button, or null when the dialog is dismissed: only the go-ahead id ever sends.</summary>
public sealed class ComposerSendGateTests
{
    private static async Task<string> Next(ComposerViewModel c)
    {
        for (var i = 0; i < 200 && c.Dialog is null; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        return c.Dialog?.Id ?? "none";
    }

    private static ComposerViewModel External(ComposerHarness h)
    {
        var c = new ComposerViewModel(h.Context with { ConfirmExternalRecipients = true }, ComposerHarness.Seed(), ComposerHarness.Identities());
        c.To.Seed("ann@x.example");
        c.Subject = "Plans";
        return c;
    }

    [Fact]
    public async Task Dismissing_the_warnings_sends_nothing()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        var sending = c.SendAsync();
        Assert.Equal("warnings", await Next(c));
        c.AnswerDialog(null);
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
        Assert.False(c.IsClosed);
        Assert.True(c.CanSend);
    }

    [Fact]
    public async Task A_single_warning_keeps_its_own_title_text_and_buttons()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        var sending = c.SendAsync();
        _ = await Next(c);
        Assert.Equal("Send without a subject?", c.Dialog!.Title);
        Assert.Equal("This message has no subject. Send it anyway?", c.Dialog.Message);
        Assert.Empty(c.Dialog.Lines);
        Assert.Equal(["Send anyway", "Back to editing"], c.Dialog.Choices.Select(x => x.Label));
        c.AnswerDialog("send");
        Assert.True(await sending);
        _ = Assert.Single(h.Sender.Sent);
    }

    [Fact]
    public async Task A_single_missing_attachment_names_the_word_and_a_single_placeholder_lists_its_chips()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        c.Subject = "Plans";
        c.FallbackBody = "see the attached file";
        var sending = c.SendAsync();
        _ = await Next(c);
        Assert.Equal("Did you forget an attachment?", c.Dialog!.Title);
        Assert.Contains("\"attached\"", c.Dialog.Message, StringComparison.Ordinal);
        c.AnswerDialog("cancel");
        Assert.False(await sending);

        c.FallbackBody = "Dear {{ name }}";
        sending = c.SendAsync();
        _ = await Next(c);
        Assert.Equal("Check placeholders?", c.Dialog!.Title);
        Assert.NotEmpty(c.Dialog.Chips);
        c.AnswerDialog(null);
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task Dismissing_the_external_recipient_question_sends_nothing_and_only_Send_anyway_sends()
    {
        var h = new ComposerHarness();
        var c = External(h);
        var sending = c.SendAsync();
        Assert.Equal("external", await Next(c));
        Assert.Equal("This recipient is outside your organisation. Send only if you mean to.", c.Dialog!.Message);
        Assert.Equal(["Send anyway", "Back"], c.Dialog.Choices.Select(x => x.Label));
        c.AnswerDialog(null);
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);

        sending = c.SendAsync();
        _ = await Next(c);
        c.AnswerDialog("cancel");
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);

        sending = c.SendAsync();
        _ = await Next(c);
        c.AnswerDialog("send");
        Assert.True(await sending);
        _ = Assert.Single(h.Sender.Sent);
    }

    [Fact]
    public async Task Warnings_and_external_recipients_are_two_questions_and_both_must_be_confirmed()
    {
        var h = new ComposerHarness();
        var c = External(h);
        c.Subject = string.Empty;
        var sending = c.SendAsync();
        Assert.Equal("warnings", await Next(c));
        c.AnswerDialog("send");
        for (var i = 0; i < 200 && c.Dialog?.Id != "external"; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal("external", c.Dialog?.Id);
        Assert.Empty(h.Sender.Sent);
        c.AnswerDialog(null);
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task Dismissing_the_send_confirmation_sends_nothing()
    {
        var h = new ComposerHarness(new ComposeSettings { SendConfirmation = true });
        var c = h.Open();
        c.To.Seed("ann@x.example");
        c.Subject = "Plans";
        var sending = c.SendAsync();
        Assert.Equal("confirm", await Next(c));
        c.AnswerDialog(null);
        Assert.False(await sending);
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public async Task Dismissing_the_schedule_dialog_or_its_warnings_schedules_nothing()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.To.Seed("ann@x.example");
        c.Subject = "Plans";
        var scheduling = c.OpenScheduleAsync();
        Assert.NotNull(c.Schedule);
        c.CancelSchedule();
        Assert.False(await scheduling);
        Assert.Empty(h.Sender.Sent);

        c.Subject = string.Empty;
        scheduling = c.OpenScheduleAsync();
        Assert.NotNull(c.Schedule);
        _ = c.Schedule!.Choose(0);
        Assert.Equal("warnings", await Next(c));
        Assert.Equal("Schedule anyway", c.Dialog!.Choices[0].Label);
        c.AnswerDialog(null);
        Assert.False(await scheduling);
        Assert.Empty(h.Sender.Sent);
        Assert.Null(c.Schedule);
    }

    [Fact]
    public async Task The_close_question_is_Bulwarks_and_dismissing_it_keeps_the_composer()
    {
        var h = new ComposerHarness();
        var c = h.Open();
        c.Subject = "Plans";
        var closing = c.RequestCloseAsync();
        Assert.Equal("leave", await Next(c));
        Assert.Equal("Save or discard draft?", c.Dialog!.Title);
        Assert.Equal("You have unsaved changes. Would you like to save this as a draft or discard it?", c.Dialog.Message);
        Assert.Equal(["Save", "Discard", "Cancel"], c.Dialog.Choices.Select(x => x.Label));
        Assert.Equal([DialogTone.Primary, DialogTone.Danger, DialogTone.Plain], c.Dialog.Choices.Select(x => x.Tone));
        c.AnswerDialog(null);
        Assert.False(await closing);
        Assert.False(c.IsClosed);
        Assert.Empty(h.Drafts.Discarded);

        var discarding = c.DiscardAsync();
        Assert.Equal("discard", await Next(c));
        Assert.Equal("Discard draft?", c.Dialog!.Title);
        Assert.Equal("The draft will be deleted.", c.Dialog.Message);
        c.AnswerDialog(null);
        Assert.False(await discarding);
        Assert.False(c.IsClosed);
        Assert.Empty(h.Drafts.Discarded);
    }
}
