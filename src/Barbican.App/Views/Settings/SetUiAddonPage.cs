// Mirrors: B-AI-001, B-AI-002, B-AI-006 (the rows of the assistant page: provider kind, base URL, model, language, action mode, memory, standing instructions, API key in the credential store), D-M6-11, D-M6-B03; decision D-M6b-08:
// one generic page draws the settings rows any native add-on declares (IPluginSettingsProvider) and its chat panel (IPluginChat). The values are the add-on's own per-account settings (IPluginSettings), the secret rows write to its secret store.

using Barbican.App.Shell;
using Barbican.Plugins;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Addons;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The settings page of a native add-on.</summary>
internal static class SetUiAddonPage
{
    public static SetUiPageFactory Create(string pageId, string pluginId)
    {
        var holder = new Grid();
        AddonPage? built = null;

        void Rebuild()
        {
            var page = ViewHost.Get<IAddonPageSource>().Find(pluginId);
            if (ReferenceEquals(page?.Settings, built?.Settings) && ReferenceEquals(page?.Chat, built?.Chat) && (page is null) == (built is null))
            {
                return;
            }

            built = page;
            holder.Children.Clear();
            holder.ColumnDefinitions.Clear();
            holder.Children.Add(page is null ? Unavailable(pageId) : Build(page));
        }

        return new SetUiPageFactory(
            pageId,
            _ =>
            {
                Rebuild();
                return holder;
            },
            () => SetUiUi.Run(async () =>
            {
                if (ViewHost.Get<IAddonPageSource>().Find(pluginId) is { } page)
                {
                    await page.Settings.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
                }

                Rebuild();
            }));
    }

    private static ScrollViewer Unavailable(string pageId)
    {
        var panel = SetUiUi.Page(SettingsNav.Find(pageId)?.Title ?? pageId);
        panel.Children.Add(SetUiUi.Meta("This add-on is turned off or has not started. Turn it on in Settings → Add-ons; changes to which add-ons run take effect after a restart."));
        return SetUiUi.Scroll(panel);
    }

    private static Grid Build(AddonPage page)
    {
        var vm = SetUiUi.Model;
        var idBase = "set.addon." + page.PluginId + ".";
        var saveGate = new SemaphoreSlim(1, 1);
        var refreshers = new List<Action<bool>>();
        var pageAccountKey = vm.AccountKey;
        var refreshing = false;
        var form = SetUiUi.Page(page.Title);
        form.Children.Add(SetUiUi.Meta(page.Intro));

        string Stored(PluginSettingRow row) => page.Settings.Read(vm.AccountKey, row.Key) ?? row.Default;

        void Save(string key, string value)
        {
            if (refreshing)
            {
                return;
            }

            var accountKey = pageAccountKey;
            SetUiUi.Run(async () =>
            {
                await saveGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
                try
                {
                    var row = page.Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
                    if (string.Equals(page.Settings.Read(accountKey, key) ?? row?.Default ?? string.Empty, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    await page.Settings.SetAsync(accountKey, new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value }, CancellationToken.None).ConfigureAwait(true);
                    var extra = page.AfterChange(accountKey, key);
                    if (extra.Count > 0)
                    {
                        await page.Settings.SetAsync(accountKey, extra, CancellationToken.None).ConfigureAwait(true);
                        if (string.Equals(accountKey, pageAccountKey, StringComparison.Ordinal))
                        {
                            Refresh(false);
                        }
                    }
                }
                finally
                {
                    _ = saveGate.Release();
                }
            });
        }

        foreach (var row in page.Rows)
        {
            var id = idBase + row.Key;
            switch (row.Kind)
            {
                case PluginSettingKind.Text:
                    {
                        var box = new TextBox { Text = Stored(row), PlaceholderText = row.Placeholder, MinWidth = 260 };
                        box.LostFocus += (_, _) => Save(row.Key, box.Text.Trim());
                        refreshers.Add(changed => { if (changed || box.FocusState == FocusState.Unfocused) box.Text = Stored(row); });
                        form.Children.Add(SetUiUi.RowOf(row.Label, row.Description, SetUiUi.Id(box, id), id + ".row"));
                        break;
                    }

                case PluginSettingKind.MultilineText:
                    {
                        var box = new TextBox { Text = Stored(row), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MinWidth = 320, PlaceholderText = row.Placeholder };
                        box.LostFocus += (_, _) => Save(row.Key, box.Text);
                        refreshers.Add(changed => { if (changed || box.FocusState == FocusState.Unfocused) box.Text = Stored(row); });
                        form.Children.Add(SetUiUi.RowOf(row.Label, row.Description, SetUiUi.Id(box, id), id + ".row"));
                        break;
                    }

                case PluginSettingKind.Choice:
                    {
                        var choices = row.Choices ?? [];
                        int IndexOf()
                        {
                            for (var i = 0; i < choices.Count; i++)
                            {
                                if (string.Equals(choices[i].Value, Stored(row), StringComparison.Ordinal)) return i;
                            }

                            return 0;
                        }
                        var choice = new SetUiChoice(choices.Select(c => c.Label), IndexOf(), index => Save(row.Key, choices[index].Value), id);
                        refreshers.Add(_ => choice.Selected = IndexOf());
                        form.Children.Add(SetUiUi.RowOf(row.Label, row.Description, SetUiUi.Id(choice, id), id + ".row"));
                        break;
                    }

                case PluginSettingKind.Toggle:
                    {
                        var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, IsOn = !string.Equals(Stored(row), "false", StringComparison.OrdinalIgnoreCase) };
                        toggle.Toggled += (_, _) => Save(row.Key, toggle.IsOn ? "true" : "false");
                        refreshers.Add(_ => toggle.IsOn = !string.Equals(Stored(row), "false", StringComparison.OrdinalIgnoreCase));
                        form.Children.Add(SetUiUi.RowOf(row.Label, row.Description, SetUiUi.Id(toggle, id), id + ".row"));
                        break;
                    }

                case PluginSettingKind.Secret:
                    AddSecretRow(form, page, row, id, refreshers);
                    break;
                default:
                    break;
            }
        }

        void Refresh(bool accountChanged)
        {
            refreshing = true;
            try
            {
                pageAccountKey = vm.AccountKey;
                foreach (var refresh in refreshers)
                {
                    refresh(accountChanged);
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(vm.AccountKey), StringComparison.Ordinal))
            {
                Refresh(true);
            }
        };
        page.Settings.Changed += (_, _) => form.DispatcherQueue.TryEnqueue(() => Refresh(false));

        var root = new Grid { ColumnSpacing = 8 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(SetUiUi.Scroll(form));
        if (page.Chat is { } chat)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            var chatModel = new AddonChatViewModel(chat, vm, ViewHost.Find<IDispatcher>());
            var column = new SetUiAddonChatColumn(page.PluginId, chatModel) { Margin = new Thickness(0, 20, 16, 16), VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(column, 1);
            root.Children.Add(column);
            _ = chatModel.InitializeAsync(CancellationToken.None);
        }

        return root;
    }

    private static void AddSecretRow(StackPanel form, AddonPage page, PluginSettingRow row, string id, List<Action<bool>> refreshers)
    {
        var key = new PasswordBox { MinWidth = 240, PlaceholderText = row.Placeholder };
        var keyStatus = SetUiUi.Meta(string.Empty);
        _ = SetUiUi.Id(keyStatus, id + ".status");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(SetUiUi.Pill("Save", () => SetUiUi.Run(async () =>
        {
            if (key.Password.Length == 0)
            {
                return;
            }

            try
            {
                await page.Secrets.WriteAsync(row.Key, key.Password, CancellationToken.None).ConfigureAwait(true);
                keyStatus.Text = "Saved in the credential store.";
            }
            finally
            {
                key.Password = string.Empty;
            }
        }), id + ".save"));
        buttons.Children.Add(SetUiUi.Pill("Remove", () => SetUiUi.Run(async () =>
        {
            await page.Secrets.WriteAsync(row.Key, null, CancellationToken.None).ConfigureAwait(true);
            keyStatus.Text = "Removed.";
        }), id + ".remove"));
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SetUiUi.Id(key, id));
        panel.Children.Add(buttons);
        refreshers.Add(changed =>
        {
            if (changed)
            {
                key.Password = string.Empty;
                keyStatus.Text = string.Empty;
            }
        });
        form.Children.Add(SetUiUi.RowOf(row.Label, row.Description, panel, id + ".row"));
        form.Children.Add(keyStatus);
    }
}
