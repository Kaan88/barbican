// Mirrors: Bulwark components/settings/security-settings.tsx, stores/account-security-store.ts (password change, two-step sign-in, app passwords, API keys); I-404..I-408. D-M6-A09: nothing typed here is logged or kept after the call, a created secret is shown once and then cleared.
// App lock, Windows Hello and the passkey rows (keyless in the schema) are M8 (D-M6-10): they are not drawn.

using Barbican.App.Shell;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The Password and sign-in page.</summary>
internal static class SetUiSecurityPage
{
    public static SetUiPageFactory Create()
    {
        var model = ViewHost.Get<SetUiSecurityViewModel>();
        var panel = SetUiUi.Page("Password & sign-in");
        var notice = SetUiUi.Meta(string.Empty);
        _ = SetUiUi.Id(notice, "set.security.status");
        var dynamic = new StackPanel { Spacing = 0 };

        var current = new PasswordBox { MinWidth = 240 };
        var next = new PasswordBox { MinWidth = 240 };
        var confirm = new PasswordBox { MinWidth = 240 };
        var totpPassword = new PasswordBox { MinWidth = 240 };
        var totpCode = new TextBox { MinWidth = 140, PlaceholderText = "6-digit code" };
        var appName = new TextBox { MinWidth = 240, PlaceholderText = "Name" };
        var keyName = new TextBox { MinWidth = 240, PlaceholderText = "Description" };
        _ = SetUiUi.Id(current, "set.security.current");
        _ = SetUiUi.Id(next, "set.security.new");
        _ = SetUiUi.Id(confirm, "set.security.confirm");
        _ = SetUiUi.Id(totpPassword, "set.security.totp.password");
        _ = SetUiUi.Id(totpCode, "set.security.totp.code");
        _ = SetUiUi.Id(appName, "set.security.app.name");
        _ = SetUiUi.Id(keyName, "set.security.key.name");

        panel.Children.Add(notice);
        panel.Children.Add(SetUiUi.Heading("Password"));
        panel.Children.Add(SetUiUi.RowOf("Current password", string.Empty, current, "set.security.current.row"));
        panel.Children.Add(SetUiUi.RowOf("New password", string.Empty, next, "set.security.new.row"));
        panel.Children.Add(SetUiUi.RowOf("Repeat the new password", string.Empty, confirm, "set.security.confirm.row"));
        panel.Children.Add(SetUiUi.Pill("Change password", () => SetUiUi.Run(async () =>
        {
            await model.ChangePasswordAsync(current.Password, next.Password, confirm.Password, CancellationToken.None).ConfigureAwait(true);
            current.Password = string.Empty;
            next.Password = string.Empty;
            confirm.Password = string.Empty;
        }), "set.security.change", primary: true));
        panel.Children.Add(dynamic);

        void Render()
        {
            notice.Text = model.Available ? model.Status : "Account security is not offered by this server.";
            dynamic.Children.Clear();
            if (!model.Available)
            {
                return;
            }

            dynamic.Children.Add(SetUiUi.Heading("Two-step sign-in"));
            dynamic.Children.Add(SetUiUi.Meta("Add an extra layer of security with a time-based one-time code."));
            if (model.TotpOn)
            {
                dynamic.Children.Add(SetUiUi.Body("Two-step sign-in is on."));
                dynamic.Children.Add(SetUiUi.RowOf("Password", "Needed to turn it off.", totpPassword, "set.security.totp.password.row"));
                dynamic.Children.Add(SetUiUi.Pill("Turn off", () => SetUiUi.Run(async () =>
                {
                    await model.DisableTotpAsync(totpPassword.Password, CancellationToken.None).ConfigureAwait(true);
                    totpPassword.Password = string.Empty;
                }), "set.security.totp.off"));
            }
            else if (model.OtpUri.Length > 0)
            {
                dynamic.Children.Add(SetUiUi.Body("Add this to your authenticator app, then enter a code."));
                var uri = new TextBox { Text = model.OtpUri, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
                dynamic.Children.Add(SetUiUi.Id(uri, "set.security.totp.uri"));
                dynamic.Children.Add(SetUiUi.RowOf("Code", string.Empty, totpCode, "set.security.totp.code.row"));
                dynamic.Children.Add(SetUiUi.Pill("Confirm", () => SetUiUi.Run(async () =>
                {
                    await model.ConfirmTotpAsync(totpCode.Text, CancellationToken.None).ConfigureAwait(true);
                    totpCode.Text = string.Empty;
                }), "set.security.totp.confirm", primary: true));
            }
            else
            {
                dynamic.Children.Add(SetUiUi.RowOf("Password", "Needed to start.", totpPassword, "set.security.totp.password.row"));
                dynamic.Children.Add(SetUiUi.Pill("Turn on", () => SetUiUi.Run(async () =>
                {
                    await model.BeginTotpAsync(totpPassword.Password, CancellationToken.None).ConfigureAwait(true);
                    totpPassword.Password = string.Empty;
                }), "set.security.totp.on"));
            }

            dynamic.Children.Add(SetUiUi.Heading("App passwords"));
            dynamic.Children.Add(SetUiUi.Meta("Passwords for apps that cannot use two-step sign-in."));
            foreach (var a in model.AppPasswords)
            {
                dynamic.Children.Add(Entry(a.Name, a.ExpiresAt is { } e ? "Expires " + e.LocalDateTime.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "No expiry", "Delete", a.Id, id => model.DeleteAppPasswordAsync(id, CancellationToken.None), "set.security.app." + a.Id));
            }

            dynamic.Children.Add(SetUiUi.RowOf("New app password", string.Empty, appName, "set.security.app.name.row"));
            dynamic.Children.Add(SetUiUi.Pill("Create app password", () => SetUiUi.Run(async () =>
            {
                await model.CreateAppPasswordAsync(appName.Text, CancellationToken.None).ConfigureAwait(true);
                appName.Text = string.Empty;
            }), "set.security.app.create"));

            dynamic.Children.Add(SetUiUi.Heading("API keys"));
            foreach (var k in model.ApiKeys)
            {
                dynamic.Children.Add(Entry(k.Description, k.ExpiresAt is { } e ? "Expires " + e.LocalDateTime.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "No expiry", "Delete", k.Id, id => model.DeleteApiKeyAsync(id, CancellationToken.None), "set.security.key." + k.Id));
            }

            dynamic.Children.Add(SetUiUi.RowOf("New API key", string.Empty, keyName, "set.security.key.name.row"));
            dynamic.Children.Add(SetUiUi.Pill("Create API key", () => SetUiUi.Run(async () =>
            {
                await model.CreateApiKeyAsync(keyName.Text, CancellationToken.None).ConfigureAwait(true);
                keyName.Text = string.Empty;
            }), "set.security.key.create"));

            if (model.IssuedSecret.Length > 0)
            {
                var secret = new TextBox { Text = model.IssuedSecret, IsReadOnly = true, MaxWidth = 520 };
                dynamic.Children.Add(SetUiUi.Heading("Copy it now"));
                dynamic.Children.Add(SetUiUi.Id(secret, "set.security.secret"));
                dynamic.Children.Add(SetUiUi.Pill("Done", model.ClearIssuedSecret, "set.security.secret.done"));
            }
        }

        model.PropertyChanged += (_, _) => Render();
        return new SetUiPageFactory(
            SettingsPageIds.Security,
            _ =>
            {
                Render();
                return SetUiUi.Scroll(panel);
            },
            () => SetUiUi.Run(() => model.RefreshAsync(CancellationToken.None)));
    }

    private static Grid Entry(string label, string description, string button, string id, Func<string, Task> action, string automationId) =>
        SetUiUi.RowOf(label, description, SetUiUi.Pill(button, () => SetUiUi.Run(() => action(id)), automationId + ".delete"), automationId);
}
