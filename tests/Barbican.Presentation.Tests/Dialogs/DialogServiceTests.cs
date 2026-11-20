// New tests: one dialog at a time, Esc cancels with null, Enter accepts a confirm and a non-empty prompt, a second dialog waits, keys are taken while open. PORT-PLAN 3.7. Owner menus.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; fakes hand back TaskCompletionSource tasks; the delays are one-tick yields

using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.Dialogs;

public sealed class DialogServiceTests
{
    [Fact]
    public async Task A_confirm_dialog_answers_true_on_enter_and_null_on_escape()
    {
        using var dialogs = new DialogService();
        var first = dialogs.ShowAsync(new ConfirmDialogRequest("Delete", "Really?", "Delete", Danger: true), CancellationToken.None);
        Assert.True(dialogs.IsOpen);
        Assert.Equal("Delete", dialogs.Current!.Title);
        Assert.True(dialogs.HandleKey(new KeyChord("Enter")));
        Assert.True(await first);
        Assert.False(dialogs.IsOpen);

        var second = dialogs.ShowAsync(new ConfirmDialogRequest("Delete", "Really?"), CancellationToken.None);
        Assert.True(dialogs.HandleKey(new KeyChord("Escape")));
        Assert.False(await second);
    }

    [Fact]
    public async Task A_prompt_dialog_answers_with_the_trimmed_text_and_ignores_enter_when_empty()
    {
        using var dialogs = new DialogService();
        var task = dialogs.ShowAsync(new PromptDialogRequest("New folder", "Name", "  "), CancellationToken.None);
        Assert.False(dialogs.HandleKey(new KeyChord("Enter")));
        Assert.True(dialogs.IsOpen);
        dialogs.Current!.Text = "  Receipts ";
        Assert.True(dialogs.HandleKey(new KeyChord("Enter")));
        Assert.Equal("Receipts", await task);
    }

    [Fact]
    public async Task A_second_dialog_waits_for_the_first()
    {
        using var dialogs = new DialogService();
        var first = dialogs.ShowAsync(new ConfirmDialogRequest("One", "a"), CancellationToken.None);
        var second = dialogs.ShowAsync(new ConfirmDialogRequest("Two", "b"), CancellationToken.None);
        Assert.Equal("One", dialogs.Current!.Title);
        dialogs.Current.Complete(true);
        Assert.True(await first);
        await Task.Delay(50);
        Assert.Equal("Two", dialogs.Current!.Title);
        dialogs.Current.Complete(null);
        Assert.False(await second);
    }

    [Fact]
    public async Task Cancelling_the_token_closes_the_dialog()
    {
        using var dialogs = new DialogService();
        using var cts = new CancellationTokenSource();
        var task = dialogs.ShowAsync(new ConfirmDialogRequest("One", "a"), cts.Token);
        await cts.CancelAsync();
        Assert.False(await task);
        Assert.False(dialogs.IsOpen);
    }

    [Fact]
    public async Task A_queued_click_after_cancellation_cannot_complete_the_dialog_again()
    {
        using var dialogs = new DialogService();
        using var cts = new CancellationTokenSource();
        var task = dialogs.ShowAsync(new ConfirmDialogRequest("One", "a"), cts.Token);
        var dialog = dialogs.Current!;
        await cts.CancelAsync();
        dialog.Complete(true);
        Assert.False(await task);
    }
}
