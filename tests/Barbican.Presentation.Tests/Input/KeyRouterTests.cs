// New tests: the M1 mail keys. Spec B-KEY-006 (single keys can be switched off), B-KEY-009, B-KEY-013 (g sequences), B-KEY-021 (stepping), hooks
// onBeforeShortcut / onAfterShortcut. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Input;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Input;

public sealed class KeyRouterTests
{
    private static async Task<ListHarness> WithFoldersAsync(string prefs = "{}")
    {
        var h = new ListHarness(prefs);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 5, 5, false)));
        h.Sidebar.Update(new MailboxTree(
        [
            new Mailbox { Id = "inbox", Name = "Inbox", Role = MailboxRole.Inbox },
            new Mailbox { Id = "sent", Name = "Sent", Role = MailboxRole.Sent },
            new Mailbox { Id = "drafts", Name = "Drafts", Role = MailboxRole.Drafts },
            new Mailbox { Id = "arch", Name = "Archive", Role = MailboxRole.Archive },
        ]));
        await h.Sidebar.SelectFolderAsync("inbox");
        return h;
    }

    [Fact]
    public async Task G_then_a_letter_goes_to_the_folder_within_the_time_limit()
    {
        using var h = await WithFoldersAsync();
        Assert.True(h.Keys.Handle(new KeyChord("g"), KeySource.Keyboard));
        h.Time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(h.Keys.Handle(new KeyChord("t"), KeySource.Keyboard));
        Assert.Equal("sent", h.List.MailboxId);
        h.Keys.Handle(new KeyChord("g"), KeySource.Keyboard);
        Assert.True(h.Keys.Handle(new KeyChord("d"), KeySource.Keyboard));
        Assert.Equal("drafts", h.List.MailboxId);
        h.Keys.Handle(new KeyChord("g"), KeySource.Keyboard);
        Assert.True(h.Keys.Handle(new KeyChord("a"), KeySource.Keyboard));
        Assert.Equal("arch", h.List.MailboxId);
    }

    [Fact]
    public async Task A_late_second_key_is_not_a_sequence()
    {
        using var h = await WithFoldersAsync();
        h.Keys.Handle(new KeyChord("g"), KeySource.Keyboard);
        h.Time.Advance(TimeSpan.FromMilliseconds(1600));
        Assert.False(h.Keys.Handle(new KeyChord("s"), KeySource.Keyboard));
        Assert.Equal("inbox", h.List.MailboxId);
    }

    [Fact]
    public async Task A_g_followed_by_a_shifted_letter_passes()
    {
        using var h = await WithFoldersAsync();
        h.Keys.Handle(new KeyChord("g"), KeySource.Keyboard);
        Assert.False(h.Keys.Handle(new KeyChord("S"), KeySource.Keyboard));
    }

    [Fact]
    public async Task Single_keys_can_be_switched_off_but_named_keys_still_work()
    {
        using var h = await WithFoldersAsync("""{"singleKeyShortcuts": false}""");
        Assert.False(h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard));
        Assert.False(h.Keys.Handle(new KeyChord("/"), KeySource.Keyboard));
        Assert.True(h.Keys.Handle(new KeyChord("Down"), KeySource.Keyboard));
        Assert.True(h.Keys.Handle(new KeyChord("Enter"), KeySource.Keyboard));
        Assert.True(h.Keys.Handle(new KeyChord("e", Control: true), KeySource.Keyboard));
    }

    [Fact]
    public async Task Slash_and_ctrl_e_ask_the_view_to_focus_the_search_box()
    {
        using var h = await WithFoldersAsync();
        var asked = 0;
        h.List.FocusSearchRequested += (_, _) => asked++;
        Assert.True(h.Keys.Handle(new KeyChord("/"), KeySource.WebView));
        Assert.True(h.Keys.Handle(new KeyChord("e", Control: true), KeySource.Keyboard));
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task Shift_g_selects_the_last_loaded_message_and_enter_opens_the_selection_again()
    {
        using var h = await WithFoldersAsync();
        Assert.True(h.Keys.Handle(new KeyChord("G"), KeySource.Keyboard));
        Assert.Equal("m4", h.List.Selected?.Id);
        Assert.True(h.Keys.Handle(new KeyChord("g", Shift: true), KeySource.Keyboard));
        Assert.True(h.Keys.Handle(new KeyChord("Enter"), KeySource.Keyboard));
        Assert.Equal("m4", h.Opener.Opened[^1].Id);
    }

    [Fact]
    public async Task Keys_from_the_web_view_and_automation_take_the_same_path()
    {
        using var h = await WithFoldersAsync();
        Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.WebView));
        Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.Automation));
        Assert.Equal("m1", h.List.Selected?.Id);
    }

    [Fact]
    public async Task Alt_chords_and_unknown_keys_are_not_ours()
    {
        using var h = await WithFoldersAsync();
        Assert.False(h.Keys.Handle(new KeyChord("j", Alt: true), KeySource.Keyboard));
        Assert.False(h.Keys.Handle(new KeyChord("q"), KeySource.Keyboard));
        Assert.False(h.Keys.Handle(new KeyChord("F5"), KeySource.Keyboard));
        Assert.False(h.Keys.Handle(new KeyChord("j", Control: true), KeySource.Keyboard));
    }

    [Fact]
    public async Task Ctrl_digits_switch_to_the_apps_that_exist()
    {
        using var h = await WithFoldersAsync();
        Assert.True(h.Keys.Handle(new KeyChord("1", Control: true), KeySource.Keyboard));
        Assert.False(h.Keys.Handle(new KeyChord("2", Control: true), KeySource.Keyboard));
        Assert.Equal(AppId.Mail, h.Shell.ActiveApp);
        h.Shell.SetAvailableApps([AppId.Mail, AppId.Calendar]);
        Assert.True(h.Keys.Handle(new KeyChord("2", Control: true), KeySource.Keyboard));
        Assert.Equal(AppId.Calendar, h.Shell.ActiveApp);
    }

    [Fact]
    public async Task An_interceptor_can_stop_a_shortcut_and_observers_hear_the_ones_that_ran()
    {
        using var h = await WithFoldersAsync();
        var after = new TaskCompletionSource<ShortcutPayload>();
        h.Bus.Intercept<ShortcutPayload>(HookNames.OnBeforeShortcut, (p, _) => ValueTask.FromResult(!string.Equals(p.Action, "next-message", StringComparison.Ordinal)));
        h.Bus.Observe<ShortcutPayload>(HookNames.OnAfterShortcut, (p, _) =>
        {
            after.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard));
        Assert.Null(h.List.Selected);
        Assert.True(h.Keys.Handle(new KeyChord("k"), KeySource.Keyboard));
        var heard = await after.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(("k", "previous-message"), (heard.Key, heard.Action));
    }
}
