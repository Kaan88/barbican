// Mirrors: Bulwark components/settings/settings-section.tsx (SettingItem: toggle, select, text, info, button), M6 plan decision D-M6-B02 (one generic renderer for the catalog rows, table-driven by SettingRowKind). A keyed row applies its value live (the shell listens to the prefs);
// a keyless row is read-only text from ISettingInfo; a row the app does not read yet says so (D-M6-A03).

using System.Text.Json.Nodes;
using Barbican.App.Theme;
using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Actions;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Contacts;
using Barbican.Core.Mail.Avatars;
using Barbican.Core.Sync;
using Barbican.Presentation.Settings;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings.Ui;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The rows of one catalog page.</summary>
public sealed class SetUiRowsView : StackPanel
{
    private readonly string _pageId;
    private readonly Func<SettingRow, bool> _include;
    private readonly ISetUiViewModel _vm = SetUiUi.Model;
    private readonly Dictionary<string, FrameworkElement> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _refresh = new(StringComparer.Ordinal);
    private bool _updating;
    private TextBlock? _error;

    public SetUiRowsView(string pageId, Func<SettingRow, bool>? include = null)
    {
        _pageId = pageId;
        _include = include ?? (_ => true);
        Spacing = 0;
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case "Values":
                    RefreshValues();
                    break;
                case nameof(ISetUiViewModel.LastError):
                    if (_error is not null)
                    {
                        _error.Text = _vm.LastError;
                        _error.Visibility = _vm.LastError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }

                    break;
                case "Info":
                case nameof(ISetUiViewModel.ShowAdvanced):
                    Render();
                    break;
            }
        };
        Render();
    }

    /// <summary>Redraws the rows (the page opened, or the filters changed).</summary>
    public void Render()
    {
        Children.Clear();
        _byId.Clear();
        _refresh.Clear();
        foreach (var r in _vm.RowsOf(_pageId))
        {
            if (!r.Visible || !_include(r.Row))
            {
                continue;
            }

            var element = Build(r);
            _byId[r.Row.Id] = element;
            Children.Add(element);
        }

        _error = SetUiUi.Meta(_vm.LastError);
        _error.Foreground = AppResources.Brush("RoseFg");
        _error.Visibility = _vm.LastError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_error);
    }

    /// <summary>Scrolls to a row and marks it (a search hit or a cross-link).</summary>
    public bool Reveal(string anchor)
    {
        if (!_byId.TryGetValue(anchor, out var element))
        {
            return false;
        }

        element.StartBringIntoView();
        if (element is Grid g)
        {
            g.Background = AppResources.Brush("Sel");
        }

        return true;
    }

    private void RefreshValues()
    {
        _updating = true;
        try
        {
            foreach (var refresh in _refresh.Values)
            {
                refresh();
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private FrameworkElement Build(SetUiRow r)
    {
        var row = r.Row;
        var id = "set.row." + row.Id;
        var desc = SetUiUi.Clean(row.Description);
        if (row.Kind == SettingRowKind.Section)
        {
            var section = new StackPanel { Spacing = 2 };
            section.Children.Add(SetUiUi.Heading(row.Label));
            if (desc.Length > 0)
            {
                section.Children.Add(SetUiUi.Meta(desc));
            }

            AutomationProperties.SetAutomationId(section, id);
            return section;
        }

        if (string.Equals(row.Id, "S-a11y.reset", StringComparison.Ordinal))
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Reset", () => SetUiUi.Run(ResetAccessibilityAsync), id + ".button"), id);
        }

        if (string.Equals(row.Id, "S-avatars.refresh", StringComparison.Ordinal))
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Refresh", () => SetUiUi.Run(RefreshAvatarsAsync), id + ".button"), id);
        }

        if (string.Equals(row.Id, "S-keyless-063", StringComparison.Ordinal))
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Refresh", () => SetUiUi.Run(RefreshDataAsync), id + ".button"), id);
        }

        if (string.Equals(row.Id, "S-keyless-030", StringComparison.Ordinal))
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Open", () => SetUiUi.Run(ShowShortcutsAsync), id + ".link"), id);
        }

        if (LinkPage(row.Id) is { } target)
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Open", () => ViewHost.Get<ISettingsNavigation>().Open(target), id + ".link"), id);
        }

        var key = SetUiRowRules.KeyOf(row);
        if (key is not null && row.Kind == SettingRowKind.Toggle)
        {
            var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, IsOn = SetUiRowRules.ToggleOn(row, r.Value) };
            toggle.Toggled += (_, _) =>
            {
                if (!_updating)
                {
                    SetUiUi.Run(() => _vm.SetValueAsync(row.Id, JsonValue.Create(SetUiRowRules.ToStored(row, toggle.IsOn)), CancellationToken.None));
                }
            };
            _refresh[row.Id] = () => toggle.IsOn = SetUiRowRules.ToggleOn(row, Current(row));
            return SetUiUi.RowOf(row.Label, Note(row, desc), SetUiUi.Id(toggle, id + ".toggle"), id);
        }

        if (key is not null && row.Kind == SettingRowKind.Select && row.Options.Count > 0)
        {
            int IndexOf(JsonNode? value) => row.Options.ToList().FindIndex(o => string.Equals(o.Label, SetUiRowRules.SelectText(row, value), StringComparison.Ordinal));
            var choice = new SetUiChoice(row.Options.Select(o => o.Label), IndexOf(r.Value), index =>
            {
                if (!_updating)
                {
                    SetUiUi.Run(() => _vm.SetValueAsync(row.Id, JsonValue.Create(row.Options[index].Value), CancellationToken.None));
                }
            });
            _refresh[row.Id] = () => choice.Selected = IndexOf(Current(row));
            return SetUiUi.RowOf(row.Label, Note(row, desc), SetUiUi.Id(choice, id + ".select"), id);
        }

        if (key is not null && row.Kind is SettingRowKind.Text or SettingRowKind.Select)
        {
            var box = new TextBox { MinWidth = 220, Text = Plain(r.Value) };
            void Commit()
            {
                if (!_updating)
                {
                    SetUiUi.Run(() => _vm.SetValueAsync(row.Id, string.IsNullOrEmpty(box.Text) ? null : JsonValue.Create(box.Text), CancellationToken.None));
                }
            }

            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Enter)
                {
                    Commit();
                }
            };
            _refresh[row.Id] = () =>
            {
                if (!box.FocusState.HasFlag(FocusState.Keyboard) && box.FocusState == FocusState.Unfocused)
                {
                    box.Text = Plain(Current(row));
                }
            };
            return SetUiUi.RowOf(row.Label, Note(row, desc), SetUiUi.Id(box, id + ".text"), id);
        }

        // keyless: an info row shows its text; a control without a key is not wired yet
        var text = r.InfoText.Length > 0 ? r.InfoText : row.Kind is SettingRowKind.Toggle or SettingRowKind.Select or SettingRowKind.Text ? "Not available yet" : string.Empty;
        var value = text.Length > 0 ? SetUiUi.Meta(text) : null;
        if (value is not null)
        {
            value.MaxWidth = 280;
            value.TextAlignment = TextAlignment.Right;
        }

        return SetUiUi.RowOf(row.Label, desc, value, id);
    }

    private static string? LinkPage(string id) => id switch
    {
        "S-keyless-027" or "S-keyless-028" => SettingsPageIds.Appearance,
        "S-keyless-029" or "S-keyless-034" => SettingsPageIds.Reading,
        "S-keyless-031" or "S-keyless-033" or "S-keyless-036" => SettingsPageIds.MessageList,
        "S-keyless-032" => SettingsPageIds.Calendar,
        "S-keyless-035" => SettingsPageIds.Composing,
        "S-keyless-037" or "S-keyless-038" => SettingsPageIds.Notifications,
        "S-keyless-061" or "S-keyless-062" or "S-keyless-064" => SettingsPageIds.Backup,
        _ => null,
    };

    private static async Task RefreshAvatarsAsync()
    {
        if (ViewHost.Find<AvatarService>() is not { } avatars)
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Sender icon refresh is unavailable.", IsError: true));
            return;
        }

        await avatars.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Sender icons refreshed."));
    }

    private static async Task RefreshDataAsync()
    {
        var accounts = ViewHost.Find<IAccountManager>();
        if (accounts?.Active is not { } account || accounts.SessionFor(account.Key) is not { } session)
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("No active account to refresh.", IsError: true));
            return;
        }

        var calendar = ViewHost.Find<ICalendarStore>();
        var contacts = ViewHost.Find<IContactStore>();
        if (session.Sync is not MailSync sync || calendar is null || contacts is null)
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Data refresh is unavailable for this account.", IsError: true));
            return;
        }

        var cal = await calendar.RefreshAsync(account.Key, CancellationToken.None).ConfigureAwait(true);
        var con = await contacts.RefreshAsync(account.Key, CancellationToken.None).ConfigureAwait(true);
        await sync.SyncNowAsync(new HashSet<string>(["Mailbox", "Email", "Thread"], StringComparer.Ordinal), CancellationToken.None).ConfigureAwait(true);
        var error = cal.Error?.Message ?? con.Error?.Message;
        _ = ViewHost.Find<IToastService>()?.Show(error is null
            ? new ToastRequest("Calendar and contacts refreshed; mail refresh requested.")
            : new ToastRequest("Refresh incomplete: " + error, IsError: true));
    }

    private static async Task ShowShortcutsAsync()
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        var context = new CommandContext(CommandSurface.Toolbar, [], null, null, null);
        if (registry is not null && registry.TryGet(CommandIds.ShowShortcuts, out var command) && command.CanExecute(context))
        {
            _ = await command.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private async Task ResetAccessibilityAsync()
    {
        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is null || !await dialogs.ShowAsync(new ConfirmDialogRequest("Reset accessibility settings", "Reset accessibility settings to their defaults? Settings shared with other pages stay unchanged.", "Reset", true), CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        var shared = _vm.CatalogPages.Where(p => !string.Equals(p.Id, SettingsPageIds.Accessibility, StringComparison.Ordinal))
            .SelectMany(p => p.Rows).Where(r => r.Key is not null).Select(r => r.Key!).ToHashSet(StringComparer.Ordinal);
        var keys = _vm.CatalogPages.Where(p => string.Equals(p.Id, SettingsPageIds.Accessibility, StringComparison.Ordinal))
            .SelectMany(p => p.Rows).Where(r => r.Key is not null && r.Kind is SettingRowKind.Toggle or SettingRowKind.Select or SettingRowKind.Text)
            .Select(r => r.Key!).Where(k => !shared.Contains(k)).Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            await _vm.SetKeyAsync(key, null, CancellationToken.None).ConfigureAwait(true);
            if (_vm.LastError.Length > 0)
            {
                return;
            }
        }

        _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Accessibility settings reset."));
    }

    private static string Note(SettingRow row, string desc) =>
        row.Status == SettingRowStatus.Unwired || string.Equals(row.Id, "S-keyless-059", StringComparison.Ordinal)
            ? (desc.Length > 0 ? desc + " " : string.Empty) + "Not used by the app yet."
            : desc;

    private JsonNode? Current(SettingRow row) => _vm.RowsOf(_pageId).FirstOrDefault(x => string.Equals(x.Row.Id, row.Id, StringComparison.Ordinal))?.Value;

    private static string Plain(JsonNode? v) => v is JsonValue j && j.TryGetValue<string>(out var s) ? s : v?.ToJsonString() ?? string.Empty;
}
