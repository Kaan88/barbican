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
using Barbican.Core.Mail.Archive;
using Barbican.Core.Sync;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Shell;
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
    private readonly List<IDisposable> _childrenToDispose = [];
    private bool _updating;
    private bool _mountainView;
    private long _accountVersion;
    private bool _archiveRunning;
    private TextBlock? _error;
    private TextBlock? _archiveStatus;

    public SetUiRowsView(string pageId, Func<SettingRow, bool>? include = null)
    {
        _pageId = pageId;
        _include = include ?? (_ => true);
        Spacing = 0;
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ISetUiViewModel.AccountKey):
                    _accountVersion++;
                    if (_archiveStatus is not null) _archiveStatus.Text = string.Empty;
                    break;
                case "Values":
                    // Mountain View hides the mail layout row (and shows it again): that changes which rows exist
                    if (SetUiRowRules.IsMountainView(_vm.ValueOf("interfaceLayout")) != _mountainView)
                    {
                        _mountainView = !_mountainView;
                        Render();
                        break;
                    }

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
        var look = StyleState.Current;
        var style = look.Style;
        look.PropertyChanged += (_, _) =>
        {
            // a style switch redraws the rows with the new look; other changes of the look do not (a redraw drops the focus of a field)
            if (look.Style == style) return;
            style = look.Style;
            Render();
        };
        Render();
    }

    /// <summary>Redraws the rows (the page opened, or the filters changed).</summary>
    public void Render()
    {
        _mountainView = SetUiRowRules.IsMountainView(_vm.ValueOf("interfaceLayout"));
        foreach (var child in _childrenToDispose) { child.Dispose(); }
        _childrenToDispose.Clear();
        Children.Clear();
        _byId.Clear();
        _refresh.Clear();
        var hidden = 0;
        FrameworkElement? previous = null;
        foreach (var r in _vm.RowsOf(_pageId))
        {
            if (!_include(r.Row))
            {
                continue;
            }

            if (!r.Visible)
            {
                if (r.Row.Advanced) hidden++;
                continue;
            }

            var element = Build(r);
            if (r.Row.Kind == SettingRowKind.Section)
            {
                EndGroup(previous);
                if (previous is null && element is StackPanel { Children: [TextBlock title, ..] }) title.Margin = new Thickness(0, 0, 0, 2);
            }
            else if (r.Row.Advanced)
            {
                AdvancedBadge(element);
            }

            _byId[r.Row.Id] = element;
            Children.Add(element);
            previous = element;
        }

        EndGroup(previous);
        if (hidden > 0 && !_vm.ShowAdvanced) Children.Add(HiddenNotice(hidden));
        _error = SetUiUi.Meta(_vm.LastError);
        _error.Foreground = AppResources.Brush("RoseFg");
        _error.Visibility = _vm.LastError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_error);
        var navigation = ViewHost.Find<ISettingsNavigation>();
        if (navigation is not null && string.Equals(navigation.Current, _pageId, StringComparison.Ordinal) && navigation.Anchor is { } anchor)
        {
            _ = Reveal(anchor);
        }
    }

    /// <summary>The last row of a section has no rule under it (settings-section.tsx:343 last:border-0).</summary>
    private static void EndGroup(FrameworkElement? last)
    {
        if (last is Grid { Padding.Top: > 0 } row) row.BorderThickness = new Thickness(0);
    }

    /// <summary>The small "Advanced" marker beside the label of a row that shows only with the switch on (settings-section.tsx:172-180).</summary>
    private static void AdvancedBadge(FrameworkElement row)
    {
        if (row is not Grid { Children: [StackPanel { Children: [TextBlock label, ..] } left, ..] }) return;
        var bulwark = StyleState.Current.IsBulwark;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = AppResources.Brush(bulwark ? "FeedbackSelected" : "Sel"),
            Child = new TextBlock { Text = "Advanced", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = AppResources.Brush(bulwark ? "FeedbackSelectedFg" : "SelText") },
        };
        left.Children.RemoveAt(0);
        left.Children.Insert(0, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, badge } });
    }

    /// <summary>"N advanced settings are hidden on this page" with the link that turns the switch on (settings-section.tsx:136-153).</summary>
    private Border HiddenNotice(int count)
    {
        var show = new Button { Content = "Show advanced settings", Style = (Style)Application.Current.Resources["Button.Icon"], Height = 24, Padding = new Thickness(8, 0, 8, 0), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = AppResources.Brush("Accent") };
        show.Click += (_, _) => _vm.ShowAdvanced = true;
        _ = SetUiUi.Id(show, "set.hidden-advanced.show");
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = count == 1 ? "1 advanced setting is hidden on this page." : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " advanced settings are hidden on this page.", FontSize = 12, Foreground = AppResources.Brush("Dim"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(show, 1);
        grid.Children.Add(show);
        var notice = new Border { Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), BorderBrush = AppResources.Brush("Line"), Background = AppResources.Brush("Input"), CornerRadius = new CornerRadius(StyleState.Current.Metrics.ButtonRadius), Child = grid };
        return SetUiUi.Id(notice, "set.hidden-advanced");
    }

    /// <summary>Scrolls to a row and marks it (a search hit or a cross-link).</summary>
    public bool Reveal(string anchor)
    {
        if (!_byId.TryGetValue(anchor, out var element))
        {
            return false;
        }

        var navigation = ViewHost.Get<ISettingsNavigation>();
        SizeChangedEventHandler? sized = null;
        RoutedEventHandler? unloaded = null;
        void Detach()
        {
            element.SizeChanged -= sized;
            element.Unloaded -= unloaded;
        }

        void TryReveal()
        {
            if (!string.Equals(navigation.Current, _pageId, StringComparison.Ordinal) || !string.Equals(navigation.Anchor, anchor, StringComparison.Ordinal))
            {
                Detach();
                return;
            }

            if (element.ActualHeight <= 0 || element.ActualWidth <= 0) return;
            Detach();
            element.StartBringIntoView();
            if (element is Grid g) g.Background = AppResources.Brush("Sel");
        }

        sized = (_, _) => TryReveal();
        unloaded = (_, _) => Detach();
        element.SizeChanged += sized;
        element.Unloaded += unloaded;
        _ = element.DispatcherQueue.TryEnqueue(TryReveal);
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
                // settings-section.tsx:231-233: text-sm muted, mt-1; space-y-4 to the rows (the rows carry 12 px of their own)
                var intro = SetUiUi.Meta(desc);
                if (StyleState.Current.IsBulwark) intro.FontSize = 14;
                section.Children.Add(intro);
            }

            section.Margin = new Thickness(0, 0, 0, StyleState.Current.IsBulwark ? 4 : 0);
            AutomationProperties.SetAutomationId(section, id);
            return section;
        }

        if (string.Equals(row.Key, "timeZone", StringComparison.Ordinal))
        {
            string Selected() => _vm.ValueOf("timeZone") is JsonValue v && v.TryGetValue<string>(out var selectedZone) ? selectedZone : "auto";
            string Label()
            {
                var currentZone = Selected();
                if (string.Equals(currentZone, "auto", StringComparison.OrdinalIgnoreCase)) return "Automatic (" + TimeZoneInfo.Local.DisplayName + ")";
                return SettingDisplayTimeZone.TryResolve(currentZone, out _) ? currentZone : currentZone + " (unavailable; using system zone)";
            }
            var button = SetUiUi.Pill(Label(), () => SetUiUi.Run(async () =>
            {
                var answer = await ViewHost.Get<IDialogService>().ShowAsync(new TimeZonePickerRequest(Selected()), CancellationToken.None).ConfigureAwait(true);
                if (answer is not null) await _vm.SetKeyAsync("timeZone", JsonValue.Create(answer), CancellationToken.None).ConfigureAwait(true);
            }), id + ".select");
            _refresh[row.Id] = () => button.Content = Label();
            return SetUiUi.RowOf(row.Label, desc, button, id);
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

        if (string.Equals(row.Id, "S-keyless-050", StringComparison.Ordinal))
        {
            var editor = new SetUiCalendarIdentityEditor(_vm);
            _childrenToDispose.Add(editor);
            return SetUiUi.RowOf(row.Label, desc, editor, id);
        }

        if (string.Equals(row.Id, "S-keyless-039", StringComparison.Ordinal))
        {
            var editor = new SetUiOrderEditor(_vm);
            _childrenToDispose.Add(editor);
            return SetUiUi.RowOf(row.Label, desc, editor, id);
        }

        if (string.Equals(row.Key, "calendarDayEndHour", StringComparison.Ordinal))
        {
            return CalendarHours(row, id);
        }

        if (row.Key is "subAddressDelimiter" or "writingLanguage")
        {
            return CustomText(row, r.Value, desc, id);
        }

        if (string.Equals(row.Id, "S-keyless-051", StringComparison.Ordinal))
        {
            return WorkingDays(row, desc, id);
        }

        if (row.Id is "S-keyless-052" or "S-keyless-053")
        {
            return SetUiUi.RowOf(row.Label, desc, SetUiUi.Pill("Open Contacts", () => ViewHost.Get<ShellViewModel>().TryActivate(AppId.Contacts), id + ".link"), id);
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
            AutomationProperties.SetName(toggle, row.Label);
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
                    // notification-settings.tsx:457-461: choosing a tone plays it
                    if (string.Equals(row.Key, "notificationSoundChoice", StringComparison.Ordinal) && row.Options[index].Value is string tone)
                    {
                        ViewHost.Find<Barbican.Core.Notifications.IDesktopNotifier>()?.PlaySound(tone);
                    }
                }
            }, id + ".select");
            _refresh[row.Id] = () => choice.Selected = IndexOf(Current(row));
            var selectRow = SetUiUi.RowOf(row.Label, Note(row, desc), SetUiUi.Id(choice, id + ".select"), id);
            if (!string.Equals(row.Key, "archiveMode", StringComparison.Ordinal)) return selectRow;
            var archiveRows = new StackPanel();
            archiveRows.Children.Add(selectRow);
            _archiveStatus = SetUiUi.Meta(string.Empty);
            _ = SetUiUi.Id(_archiveStatus, "set.archive.status");
            archiveRows.Children.Add(SetUiUi.RowOf("Reorganize existing archive", "Move messages in the Archive root into folders using the selected mode.", SetUiUi.Pill("Reorganize", () => SetUiUi.Run(ReorganizeArchiveAsync), "set.archive.reorganize"), "set.archive.reorganize.row"));
            archiveRows.Children.Add(_archiveStatus);
            return archiveRows;
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
        var unavailable = row.Id is "S-keyless-019" or "S-keyless-020" or "S-keyless-021" or "S-keyless-022" or "S-keyless-023" or "S-keyless-024" or "S-keyless-040";
        var text = unavailable ? "Not available yet" : r.InfoText.Length > 0 ? r.InfoText : row.Kind is SettingRowKind.Toggle or SettingRowKind.Select or SettingRowKind.Text ? "Not available yet" : string.Empty;
        var value = text.Length > 0 ? SetUiUi.Meta(text) : null;
        if (value is not null)
        {
            value.MaxWidth = 280;
            value.TextAlignment = TextAlignment.Right;
        }

        return SetUiUi.RowOf(row.Label, desc, value, id);
    }

    private Grid CustomText(SettingRow row, JsonNode? value, string desc, string id)
    {
        var box = new TextBox { MinWidth = 160, Text = Plain(value), PlaceholderText = string.Equals(row.Key, "writingLanguage", StringComparison.Ordinal) ? "auto or language tag" : "+" };
        AutomationProperties.SetName(box, row.Label);
        AutomationProperties.SetAutomationId(box, id + ".text");
        box.LostFocus += (_, _) => SetUiUi.Run(() => _vm.SetValueAsync(row.Id, JsonValue.Create(box.Text.Trim()), CancellationToken.None));
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { SetUiUi.Run(() => _vm.SetValueAsync(row.Id, JsonValue.Create(box.Text.Trim()), CancellationToken.None)); }
        };
        _refresh[row.Id] = () => { if (box.FocusState == FocusState.Unfocused) { box.Text = Plain(Current(row)); } };
        return SetUiUi.RowOf(row.Label, desc, box, id);
    }

    private Grid CalendarHours(SettingRow row, string id)
    {
        var start = new TextBox { Width = 72, Header = "From" };
        var end = new TextBox { Width = 72, Header = "Until" };
        AutomationProperties.SetAutomationId(start, id + ".start");
        AutomationProperties.SetAutomationId(end, id + ".end");
        void Commit(TextBox box, bool isStart)
        {
            if (!int.TryParse(box.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hour))
            {
                _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Enter a whole hour.", IsError: true));
                return;
            }

            SetUiUi.Run(() => _vm.SetCalendarHourAsync(isStart, hour, CancellationToken.None));
        }

        start.LostFocus += (_, _) => Commit(start, true);
        end.LostFocus += (_, _) => Commit(end, false);
        start.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(start, true); } };
        end.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(end, false); } };
        void Refresh()
        {
            if (start.FocusState == FocusState.Unfocused) { start.Text = Plain(_vm.ValueOf("calendarDayStartHour") ?? JsonValue.Create(8)); }
            if (end.FocusState == FocusState.Unfocused) { end.Text = Plain(_vm.ValueOf("calendarDayEndHour") ?? JsonValue.Create(20)); }
        }

        _refresh[row.Id] = Refresh;
        Refresh();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(start);
        panel.Children.Add(end);
        return SetUiUi.RowOf("Visible hours", "Whole hours from 0 to 24; the end stays after the start.", panel, id);
    }

    private Grid WorkingDays(SettingRow row, string desc, string id)
    {
        string[] names = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var boxes = new Dictionary<int, CheckBox>();
        var first = _vm.ValueOf("firstDayOfWeek") is JsonValue firstValue && firstValue.TryGetValue<int>(out var firstDay) ? Math.Clamp(firstDay, 0, 6) : 1;
        for (var i = 0; i < names.Length; i++)
        {
            var day = (first + i) % 7;
            var box = new CheckBox { Content = names[day] };
            AutomationProperties.SetAutomationId(box, id + ".day." + day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            box.Checked += (_, _) => { if (!_updating) { SetUiUi.Run(() => _vm.SetWorkingDayAsync(day, true, CancellationToken.None)); } };
            box.Unchecked += (_, _) => { if (!_updating) { SetUiUi.Run(() => _vm.SetWorkingDayAsync(day, false, CancellationToken.None)); } };
            boxes.Add(day, box);
            panel.Children.Add(box);
        }

        void Refresh()
        {
            var stored = _vm.ValueOf("calendarWorkingDays") as JsonArray;
            var days = stored is null ? new HashSet<int>([1, 2, 3, 4, 5]) : stored.OfType<JsonValue>().Select(v => v.TryGetValue<int>(out var d) ? d : -1).ToHashSet();
            foreach (var (day, box) in boxes) { box.IsChecked = days.Contains(day); }
        }

        _refresh[row.Id] = Refresh;
        _updating = true;
        try { Refresh(); } finally { _updating = false; }
        return SetUiUi.RowOf(row.Label, desc, panel, id);
    }

    private static string? LinkPage(string id) => id switch
    {
        "S-keyless-026" => SettingsPageIds.Accessibility,
        "S-keyless-027" or "S-keyless-028" => SettingsPageIds.Appearance,
        "S-keyless-029" or "S-keyless-034" => SettingsPageIds.Reading,
        "S-keyless-031" or "S-keyless-033" or "S-keyless-036" => SettingsPageIds.MessageList,
        "S-keyless-032" => SettingsPageIds.Calendar,
        "S-keyless-035" => SettingsPageIds.Composing,
        "S-keyless-037" or "S-keyless-038" => SettingsPageIds.Notifications,
        "S-keyless-061" or "S-keyless-062" or "S-keyless-064" => SettingsPageIds.Backup,
        _ => null,
    };

    private async Task ReorganizeArchiveAsync()
    {
        var accounts = ViewHost.Find<IAccountManager>();
        var service = ViewHost.Find<IArchiveReorganizer>();
        var dialogs = ViewHost.Find<IDialogService>();
        var account = accounts?.Active?.Key;
        var version = _accountVersion;
        if (_archiveRunning || account is null || service is null || dialogs is null)
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Archive reorganization is unavailable.", IsError: true));
            return;
        }

        var storedMode = _vm.ValueOf("archiveMode") as JsonValue;
        var mode = storedMode is not null && storedMode.TryGetValue<string>(out var selectedMode) ? selectedMode ?? "single" : "single";
        _archiveRunning = true;
        try
        {
            if (!await dialogs.ShowAsync(new ConfirmDialogRequest("Reorganize archive", "Move messages in this account's Archive root using the selected folder mode?", "Reorganize", true), CancellationToken.None).ConfigureAwait(true)
                || version != _accountVersion || !string.Equals(accounts?.Active?.Key, account, StringComparison.Ordinal)) return;
            var result = await service.ReorganizeAsync(account, mode, CancellationToken.None).ConfigureAwait(true);
            if (version != _accountVersion || !string.Equals(accounts?.Active?.Key, account, StringComparison.Ordinal)) return;
            if (accounts?.SessionFor(account)?.Sync is MailSync sync)
            {
                await sync.SyncNowAsync(new HashSet<string>(["Mailbox", "Email", "Thread"], StringComparer.Ordinal), CancellationToken.None).ConfigureAwait(true);
            }

            if (version != _accountVersion || !string.Equals(accounts?.Active?.Key, account, StringComparison.Ordinal)) return;
            var report = result.Value;
            var message = report is null ? result.Error?.Message ?? "Archive reorganization did not produce a report." :
                string.Equals(mode, "single", StringComparison.Ordinal) && result.IsOk ? "Single-folder mode needs no reorganization." :
                $"Moved {report.Moved}; skipped {report.Skipped}; failed {report.Failed}; unattempted {report.Unattempted}; unknown {report.Unknown}. " +
                (report.SnapshotComplete ? $"Snapshot total {report.Total}. " : "Snapshot incomplete. ") +
                (report.Cancelled ? "Cancelled. " : string.Empty) + (report.Detail ?? string.Empty) + " " + (result.Error?.Message ?? string.Empty);
            if (_archiveStatus is not null) _archiveStatus.Text = message;
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest(message, IsError: !result.IsOk || report?.Failed > 0 || report?.Unknown > 0));
        }
        finally { _archiveRunning = false; }
    }

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
