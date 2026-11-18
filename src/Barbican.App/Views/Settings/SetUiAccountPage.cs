// Mirrors: Bulwark components/settings/account-settings.tsx (the signed-in account, storage usage, the logged-in accounts with sign out, the display name, the link to the addresses); I-399..I-403 (account list, sign out, name and addresses). The addresses themselves are srv-ui's Identities page: "Manage addresses" opens it through ISettingsNavigation.

using Barbican.App.Shell;
using Barbican.Core.Accounts;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The Account page.</summary>
internal static class SetUiAccountPage
{
    public static SetUiPageFactory Create()
    {
        var model = ViewHost.Get<SetUiSecurityViewModel>();
        var accounts = ViewHost.Find<IAccountManager>();
        var panel = SetUiUi.Page("Account");
        var info = new StackPanel();
        var storage = new StackPanel { Spacing = 6 };
        var list = new StackPanel();
        var name = new TextBox { MinWidth = 220 };
        _ = SetUiUi.Id(name, "set.account.name");
        var status = SetUiUi.Meta(string.Empty);
        _ = SetUiUi.Id(status, "set.account.status");

        panel.Children.Add(SetUiUi.Heading("Your account"));
        panel.Children.Add(info);
        panel.Children.Add(SetUiUi.Heading("Storage"));
        panel.Children.Add(storage);
        panel.Children.Add(SetUiUi.Heading("Logged-in accounts"));
        panel.Children.Add(list);
        panel.Children.Add(SetUiUi.Heading("Your name"));
        panel.Children.Add(SetUiUi.RowOf("Name", "People see this name next to your address when you send them mail.", name, "set.account.name.row"));
        panel.Children.Add(SetUiUi.Pill("Save name", () => SetUiUi.Run(() => model.SetDisplayNameAsync(name.Text, CancellationToken.None)), "set.account.name.save"));
        panel.Children.Add(SetUiUi.Heading("Email addresses"));
        panel.Children.Add(SetUiUi.Meta("The addresses you can send from, each with its own name and signature."));
        panel.Children.Add(SetUiUi.Pill("Manage addresses", () => ViewHost.Get<ISettingsNavigation>().Open(SettingsPageIds.Identities), "set.account.addresses"));
        panel.Children.Add(status);
        var rows = new SetUiRowsView(SettingsPageIds.Account, r => SetUiRowRules.KeyOf(r) is not null);
        panel.Children.Add(rows);

        void Render()
        {
            info.Children.Clear();
            var active = accounts?.Active;
            info.Children.Add(SetUiUi.RowOf("Email address", string.Empty, SetUiUi.Meta(active?.User ?? "sample@example.org"), "set.account.email"));
            info.Children.Add(SetUiUi.RowOf("JMAP server", string.Empty, SetUiUi.Meta(active?.Server ?? "sample.example.org"), "set.account.server"));
            info.Children.Add(SetUiUi.RowOf("Authentication", string.Empty, SetUiUi.Meta(active is null ? "Password" : string.Equals(active.Kind, "oauth", StringComparison.Ordinal) ? "Single sign-on" : "Password"), "set.account.auth"));

            storage.Children.Clear();
            if (model.QuotaText.Length > 0)
            {
                storage.Children.Add(SetUiUi.Body(model.QuotaText));
                storage.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = model.QuotaFraction, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left });
            }
            else
            {
                storage.Children.Add(SetUiUi.Meta("Storage use is not available."));
            }

            list.Children.Clear();
            var all = accounts?.Accounts ?? [];
            if (all.Count == 0)
            {
                list.Children.Add(SetUiUi.Meta("Sample account (no sign-in in this run)."));
            }

            foreach (var a in all)
            {
                var key = a.Key;
                var isActive = accounts?.Active is { } act && string.Equals(act.Key, key, StringComparison.Ordinal);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (!isActive)
                {
                    buttons.Children.Add(SetUiUi.Pill("Switch", () => SetUiUi.Run(async () => _ = await accounts!.SwitchToAsync(key, CancellationToken.None).ConfigureAwait(true)), "set.account.switch." + key));
                }

                buttons.Children.Add(SetUiUi.Pill("Sign out", () => SetUiUi.Run(() => SignOutAsync(key, a.User)), "set.account.signout." + key));
                list.Children.Add(SetUiUi.RowOf(a.User + (isActive ? " (active)" : string.Empty), a.Server, buttons, "set.account.row." + key));
            }

            status.Text = model.Status;
            if (!name.FocusState.HasFlag(FocusState.Keyboard) && name.FocusState == FocusState.Unfocused)
            {
                name.Text = model.DisplayName;
            }
        }

        model.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(model.AccountKey), StringComparison.Ordinal))
            {
                name.Text = string.Empty;
            }

            Render();
        };
        return new SetUiPageFactory(
            SettingsPageIds.Account,
            f =>
            {
                f.Rows = rows;
                Render();
                return SetUiUi.Scroll(panel);
            },
            () => SetUiUi.Run(async () =>
            {
                await model.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
                await SetUiUi.Model.RefreshInfoAsync(SettingsPageIds.Account, CancellationToken.None).ConfigureAwait(true);
            }));
    }

    private static async Task SignOutAsync(string key, string user)
    {
        var maintenance = ViewHost.Find<IAccountMaintenance>();
        if (maintenance is null)
        {
            return;
        }

        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is not null)
        {
            var yes = await dialogs.ShowAsync(new ConfirmDialogRequest("Sign out", "Sign " + user + " out of Barbican on this device?", "Sign out", true), CancellationToken.None).ConfigureAwait(true);
            if (!yes)
            {
                return;
            }
        }

        _ = await maintenance.SignOutAsync(key, CancellationToken.None).ConfigureAwait(true);
    }
}
