// New tests: the shell state, the app lifecycle hooks and the start-up. Spec B-MOT-001..004, hooks of the app, navigation and ui groups. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Shell;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Default_style_is_bulwark_and_explicit_legacy_choices_survive()
    {
        using var h = new ListHarness("{}");
        Assert.Equal("Bulwark", h.Shell.Style.ToString());
        Assert.Equal(ThemeVariant.Default, h.Shell.Variant);
        h.Prefs.Set("""{"uiStyle":"bento"}""");
        Assert.Equal(ShellStyle.Bento, h.Shell.Style);
        h.Prefs.Set("""{"uiStyle":"nordic"}""");
        Assert.Equal(ShellStyle.Nordic, h.Shell.Style);
    }

    [Fact]
    public void Bulwark_theme_setting_follows_system_or_explicit_scheme()
    {
        using var h = new ListHarness("{}");
        h.Shell.SetSystemDark(false);
        Assert.Equal(ThemeVariant.Default with { Dark = false }, h.Shell.Variant);
        h.Prefs.Set("""{"theme":"dark","contrast":"more"}""");
        Assert.Equal(ThemeVariant.Default with { MoreContrast = true }, h.Shell.Variant);
        h.Shell.SetSystemDark(false);
        Assert.True(h.Shell.Dark);
        h.Prefs.Set("""{"theme":"light"}""");
        h.Shell.SetSystemDark(true);
        Assert.False(h.Shell.Dark);
    }

    [Fact]
    public void The_look_comes_from_the_settings_and_the_os()
    {
        using var h = new ListHarness("""{"uiStyle": "nordic", "theme": "light", "contrast": "more"}""");
        Assert.Equal(ShellStyle.Nordic, h.Shell.Style);
        Assert.False(h.Shell.Dark);
        Assert.True(h.Shell.MoreContrast);
        Assert.Equal(new ThemeVariant(Dark: false, MoreContrast: true, Nordic: true), h.Shell.Variant);
        h.Prefs.Set("""{"theme": "system"}""");
        h.Shell.SetSystemDark(false);
        Assert.False(h.Shell.Dark);
        h.Shell.SetSystemDark(true);
        Assert.True(h.Shell.Dark);
        Assert.Equal(ShellStyle.Bulwark, h.Shell.Style);
    }

    [Fact]
    public void Reduce_motion_follows_the_setting_over_the_os()
    {
        using var h = new ListHarness("""{"reduceMotion": "on"}""");
        Assert.True(h.Shell.ReduceMotion);
        h.Prefs.Set("""{"reduceMotion": "off"}""");
        Assert.False(h.Shell.ReduceMotion);
        h.Prefs.Set("{}");
        Assert.False(h.Shell.ReduceMotion);
    }

    [Fact]
    public void Preview_look_changes_nothing_stored()
    {
        using var h = new ListHarness();
        h.Shell.PreviewLook(ShellStyle.Nordic, dark: false);
        Assert.Equal(new ThemeVariant(false, false, true), h.Shell.Variant);
        Assert.Equal(ShellStyle.Bento, ShellStyleFrom(h));
    }

    private static ShellStyle ShellStyleFrom(ListHarness h) =>
        string.Equals(h.Prefs.Current.Str("uiStyle", "bento"), "nordic", StringComparison.Ordinal) ? ShellStyle.Nordic : ShellStyle.Bento;

    [Fact]
    public void The_window_title_carries_the_unread_count_when_the_badge_is_on()
    {
        using var h = new ListHarness();
        h.Shell.SetUnreadBadge(5);
        Assert.Equal("(5) Barbican", h.Shell.WindowTitle);
        h.Prefs.Set("""{"faviconUnreadBadge": false}""");
        h.Shell.SetUnreadBadge(5);
        Assert.Equal("Barbican", h.Shell.WindowTitle);
    }

    // MA0051: one scenario that touches every hook of the group; splitting it would only move the lines.
#pragma warning disable MA0051
    [Fact]
    public async Task Navigation_and_lifecycle_fire_their_hooks()
    {
        using var h = new ListHarness();
        var seen = new List<string>();
        void Watch<T>(string hook) => h.Bus.Observe<T>(hook, (_, _) =>
        {
            lock (seen)
            {
                seen.Add(hook);
            }

            return ValueTask.CompletedTask;
        });
        foreach (var hook in new[] { HookNames.OnNavigate, HookNames.OnRouteEnter, HookNames.OnRouteLeave })
        {
            Watch<RoutePayload>(hook);
        }

        Watch<SidebarAppPayload>(HookNames.OnSidebarAppOpen);
        Watch<SidebarAppPayload>(HookNames.OnSidebarAppClose);

        Watch<EmptyPayload>(HookNames.OnWindowFocus);
        Watch<EmptyPayload>(HookNames.OnWindowBlur);
        Watch<EmptyPayload>(HookNames.OnOnline);
        Watch<EmptyPayload>(HookNames.OnOffline);
        Watch<EmptyPayload>(HookNames.OnBeforeUnload);
        Watch<AppReadyPayload>(HookNames.OnAppReady);
        Watch<VisibilityPayload>(HookNames.OnVisibilityChange);
        Watch<AppErrorPayload>(HookNames.OnAppError);
        Watch<IntervalPayload>(HookNames.OnInterval);
        Watch<SidebarTogglePayload>(HookNames.OnSidebarToggle);
        Watch<SidebarCollapsePayload>(HookNames.OnSidebarCollapse);

        h.Shell.SetAvailableApps([AppId.Mail, AppId.Calendar]);
        h.Shell.TryActivate(AppId.Calendar);
        h.Shell.NotifyWindowActive(true);
        h.Shell.NotifyWindowActive(false);
        h.Shell.NotifyOnline(true);
        h.Shell.NotifyOnline(false);
        h.Shell.NotifyBeforeClose();
        h.Shell.NotifyReady("acct");
        h.Shell.NotifyVisibility(false);
        h.Shell.ReportError(new string('x', 800));
        h.Shell.NotifyInterval();
        h.Shell.ToggleSidebar();
        h.Shell.ToggleSidebarCollapsed();
        for (var i = 0; i < 100 && SeenCount(seen) < 16; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        string[] all =
        [
            HookNames.OnNavigate, HookNames.OnRouteEnter, HookNames.OnRouteLeave, HookNames.OnSidebarAppOpen, HookNames.OnSidebarAppClose,
            HookNames.OnWindowFocus, HookNames.OnWindowBlur, HookNames.OnOnline, HookNames.OnOffline, HookNames.OnBeforeUnload, HookNames.OnAppReady,
            HookNames.OnVisibilityChange, HookNames.OnAppError, HookNames.OnInterval, HookNames.OnSidebarToggle, HookNames.OnSidebarCollapse,
        ];
        lock (seen)
        {
            Assert.Equal([.. all.Order(StringComparer.Ordinal)], [.. seen.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }

    }

#pragma warning restore MA0051
    private static int SeenCount(List<string> seen)
    {
        lock (seen)
        {
            return seen.Count;
        }
    }

    [Fact]
    public void An_app_that_does_not_exist_yet_does_not_open()
    {
        using var h = new ListHarness();
        Assert.False(h.Shell.TryActivate(AppId.Calendar));
        Assert.Equal(AppId.Mail, h.Shell.ActiveApp);
    }

    [Fact]
    public async Task Start_up_loads_the_folders_opens_the_inbox_and_fills_the_tiles()
    {
        using var h = new ListHarness();
        h.Service.Tree = new MailboxTree([new Mailbox { Id = "inbox", Name = "Inbox", Role = MailboxRole.Inbox, UnreadEmails = 2, TotalEmails = 9 }]);
        h.Service.Quota = new QuotaInfo(2_000_000_000, 10_000_000_000);
        h.Service.Identities = [new Identity("i", "Ada Lovelace", "ada@a.example.com")];
        h.Service.OnQuery = (q, _, _) => Task.FromResult(Result<ListPage>.Ok(!string.Equals(q.HasKeyword, "$flagged", StringComparison.Ordinal) ? Mails.Page(0, 3, 3, false) : new ListPage { Items = [Mails.Make("s1", from: "Bob Smith"), Mails.Make("s2", from: "Cy Young")], Total = 7 }));
        var glance = new GlanceTilesViewModel(h.Time);
        using var startup = new MailShellStartup(h.Active, h.Sidebar, glance, h.Shell, h.Time);
        await startup.StartAsync(CancellationToken.None);
        Assert.Equal("inbox", h.List.MailboxId);
        Assert.Equal(3, h.List.Messages.Count());
        Assert.Equal("(2) Barbican", h.Shell.WindowTitle);
        Assert.Equal("2.0 GB of 10 GB", h.Sidebar.StorageText);
        Assert.Equal("Inbox", glance.ListTitle);
        Assert.Equal("Good afternoon, Ada. 2 unread messages.", glance.ListSubtitle);
        Assert.Equal((7, "Bob, Cy"), (glance.FlaggedCount, glance.FlaggedWho));
    }
}
