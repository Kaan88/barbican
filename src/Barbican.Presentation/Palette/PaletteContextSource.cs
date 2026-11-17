// Mirrors: Rust palette.rs context :230; Bulwark components/command-palette (the open app, the open message, the accounts, capability-gated commands). Owner menus.

using Barbican.Core.Accounts;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Palette;

/// <summary>The palette's context from the shell, the selection and the accounts.</summary>
public sealed class PaletteContextSource(ShellViewModel shell, IMailSelection selection, IAccountManager? accounts = null) : IPaletteContextSource
{
    /// <inheritdoc />
    public PaletteContext Current()
    {
        var target = selection.Current.Count > 0 ? selection.Current[0] : null;
        var app = shell.ActiveApp;
        return new PaletteContext
        {
            App = app switch { AppId.Calendar => 1, AppId.Contacts => 2, AppId.Files => 3, _ => 0 },
            SettingsOpen = app == AppId.Settings,
            Targets = selection.Current,
            HasMail = target is not null,
            MailUnread = target is not null && !target.Keywords.IsSet("$seen"),
            MailFlagged = target?.Keywords.IsSet("$flagged") ?? false,
            Calendars = shell.AvailableApps.Contains(AppId.Calendar),
            Contacts = shell.AvailableApps.Contains(AppId.Contacts),
            Files = shell.AvailableApps.Contains(AppId.Files),
            Dark = shell.Dark,
            SingleKeys = shell.Accessibility.SingleKeys,
            Accounts = accounts?.Accounts.Select(a => a.User).ToList() ?? [],
            Account = accounts?.Active?.User ?? string.Empty,
        };
    }
}
