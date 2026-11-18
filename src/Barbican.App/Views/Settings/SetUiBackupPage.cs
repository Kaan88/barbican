// Mirrors: Bulwark components/settings/about-data-settings.tsx (Settings backup: export, import; Fix problems: reset), Rust settings.rs:167 import; I-449, I-450. The file is a path typed here (the OS pickers are not used: D-M6-B06); reset asks first in the in-window dialog.

using Barbican.App.Shell;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The Backup and import page.</summary>
internal static class SetUiBackupPage
{
    public static SetUiPageFactory Create()
    {
        var model = ViewHost.Get<SetUiBackupViewModel>();
        var panel = SetUiUi.Page("Backup & import");
        var path = new TextBox { MinWidth = 340, Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "barbican-settings.json") };
        var status = SetUiUi.Meta(string.Empty);
        _ = SetUiUi.Id(path, "set.backup.path");
        _ = SetUiUi.Id(status, "set.backup.status");
        panel.Children.Add(SetUiUi.Heading("Settings backup"));
        panel.Children.Add(SetUiUi.Meta("Export your settings to a file and import them on another device or after a reset. Passwords and keys are never in the file."));
        panel.Children.Add(SetUiUi.RowOf("File", "Where to write the backup or read it from.", path, "set.backup.path.row"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SetUiUi.Pill("Export", () => SetUiUi.Run(async () =>
        {
            var (text, outcome) = await model.ExportAsync(CancellationToken.None).ConfigureAwait(true);
            if (text is not null)
            {
                try
                {
                    await File.WriteAllTextAsync(path.Text, text, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    outcome = "Could not write the file: " + e.Message;
                }
            }

            status.Text = outcome;
        }), "set.backup.export", primary: true));
        buttons.Children.Add(SetUiUi.Pill("Import", () => SetUiUi.Run(async () =>
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(path.Text, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                status.Text = "Could not read the file: " + e.Message;
                return;
            }

            status.Text = await model.ImportAsync(text, CancellationToken.None).ConfigureAwait(true);
        }), "set.backup.import"));
        panel.Children.Add(buttons);
        panel.Children.Add(SetUiUi.Heading("Fix problems"));
        panel.Children.Add(SetUiUi.Meta("Restore every setting to its default. Your mail and accounts are not touched."));
        panel.Children.Add(SetUiUi.Pill("Reset settings", () => SetUiUi.Run(async () =>
        {
            var outcome = await model.ResetAsync(CancellationToken.None).ConfigureAwait(true);
            if (outcome.Length > 0)
            {
                status.Text = outcome;
            }
        }), "set.backup.reset"));
        panel.Children.Add(status);
        return new SetUiPageFactory(SettingsPageIds.Backup, _ => SetUiUi.Scroll(panel));
    }
}
