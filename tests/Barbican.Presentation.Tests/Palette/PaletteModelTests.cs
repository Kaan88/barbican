// Ported from Rust src/palette.rs tests (:614-:682). Implemented by stream menus: PaletteModel (M2 plan 6.2, T-201..T-205).

using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Tests.Palette;

public sealed class PaletteModelTests
{
    private static bool Eq(string actual, string expected) => string.Equals(actual, expected, StringComparison.Ordinal);

    private static PaletteContext Base() => new()
    {
        Calendars = true,
        Contacts = true,
        Files = true,
        SingleKeys = true,
        Dark = true,
        Account = "me@example.org",
        Accounts = ["me@example.org", "other@example.net"],
    };

    [Fact]
    [RustTest("palette::matches_every_word")]
    public void Matches_every_word()
    {
        Assert.True(PaletteModel.CommandMatches("go cal", "Go to Calendar", []));
        Assert.True(PaletteModel.CommandMatches(string.Empty, "Anything", []));
        Assert.True(PaletteModel.CommandMatches("files", "Search everything", ["Contacts", "Files"]));
        Assert.False(PaletteModel.CommandMatches("go mail x", "Go to Mail", []));
    }

    [Fact]
    [RustTest("palette::app_commands_come_first")]
    public void App_commands_come_first()
    {
        var x = Base() with { App = 1, CalView = 1 };
        var v = PaletteModel.Commands(x);
        Assert.Equal("Calendar", v[0].Group);
        Assert.Equal("New event", v[0].Label);

        // the current view is not offered
        Assert.DoesNotContain(v, c => Eq(c.Label, "Week view"));
        Assert.Contains(v, c => Eq(c.Label, "Day view") && Eq(c.Shortcut, "d"));

        // compose has no "c" hint outside Mail (Calendar's n / c means new event)
        Assert.Equal(string.Empty, v.First(c => Eq(c.Id, "compose")).Shortcut);
    }

    [Fact]
    [RustTest("palette::mail_commands_need_an_open_message")]
    public void Mail_commands_need_an_open_message()
    {
        var x = Base();
        Assert.DoesNotContain(PaletteModel.Commands(x), c => Eq(c.Group, "Mail"));

        x = x with { HasMail = true, MailUnread = true };
        var v = PaletteModel.Commands(x);
        Assert.Contains(v, c => Eq(c.Id, "mail:read"));
        Assert.DoesNotContain(v, c => Eq(c.Id, "mail:unread"));
        Assert.Equal("c", v.First(c => Eq(c.Id, "compose")).Shortcut);

        x = x with { SingleKeys = false };
        Assert.Equal(string.Empty, PaletteModel.Commands(x).First(c => Eq(c.Id, "mail:reply")).Shortcut);
    }

    [Fact]
    [RustTest("palette::accounts_and_capabilities")]
    public void Accounts_and_capabilities()
    {
        var x = Base();
        var v = PaletteModel.Commands(x);
        Assert.Contains(v, c => Eq(c.Label, "Switch to other@example.net"));
        Assert.DoesNotContain(v, c => Eq(c.Label, "Switch to me@example.org"));
        Assert.Equal("Sign out of me@example.org", v[^1].Label);

        x = x with { Files = false };
        Assert.DoesNotContain(PaletteModel.Commands(x), c => Eq(c.Id, "go:files"));
        Assert.Equal("Switch to light theme", PaletteModel.Filter(PaletteModel.Commands(x), "theme")[0].Label);
    }

    [Fact]
    [RustTest("palette::settings_pages")]
    public void Settings_pages()
    {
        var x = Base() with
        {
            SettingsOpen = true,
            SettingsTab = "general",
            SettingsTabs = [("general", "General"), ("appearance", "Appearance")],
        };
        var v = PaletteModel.Commands(x);
        Assert.Equal("Go to Appearance settings", v[0].Label);
        Assert.DoesNotContain(v, c => Eq(c.Label, "Go to General settings"));
    }
}
