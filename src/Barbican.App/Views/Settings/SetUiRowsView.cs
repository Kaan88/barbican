// Mirrors: Bulwark components/settings/settings-section.tsx (SettingItem: toggle, select, text, info, button), M6 plan decision D-M6-B02 (one generic renderer for the catalog rows, table-driven by SettingRowKind). A keyed row applies its value live (the shell listens to the prefs);
// a keyless row is read-only text from ISettingInfo; a row the app does not read yet says so (D-M6-A03).

using System.Text.Json.Nodes;
using Barbican.App.Theme;
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

    private static string Note(SettingRow row, string desc) => row.Status == SettingRowStatus.Unwired ? (desc.Length > 0 ? desc + " " : string.Empty) + "Not used by the app yet." : desc;

    private JsonNode? Current(SettingRow row) => _vm.RowsOf(_pageId).FirstOrDefault(x => string.Equals(x.Row.Id, row.Id, StringComparison.Ordinal))?.Value;

    private static string Plain(JsonNode? v) => v is JsonValue j && j.TryGetValue<string>(out var s) ? s : v?.ToJsonString() ?? string.Empty;
}
