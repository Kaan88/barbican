// Mirrors: Bulwark components/settings/filter-settings.tsx (the list: enable, reorder, edit, delete, add; "apply to existing mail"; the Sieve view; the lock notice of a hand-edited script, B-SIEVE-007), Rust pim-filters.slint; I-432..I-434. The rows come from ISrvUiFiltersViewModel (a model that posts to the UI thread); the page only draws.
// Deleting a rule asks first (D-7). Owner srv-ui.

using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Filters settings page.</summary>
internal sealed class SrvUiFiltersPage : SrvUiPage
{
    private readonly ISrvUiFiltersViewModel _model;

    public SrvUiFiltersPage(ISrvUiFiltersViewModel model)
        : base(SettingsPageIds.Filters, "Filters", "Rules run on the server, in this order, when mail arrives.")
    {
        _model = model;
        _model.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync() => _model.LoadAsync(CancellationToken.None);

    protected override void Render()
    {
        Reset();
        if (_model.LoadError is { } loadError)
        {
            Body.Children.Add(SrvUiKit.Error("Filters could not be loaded: " + loadError));
        }

        if (_model.Locked)
        {
            var notice = SrvUiKit.Card(SrvUiKit.Dim("This script was edited by hand, so the rules are shown read-only. Edit the Sieve script on the server to change them."), "srv.filters.locked");
            Body.Children.Add(notice);
        }

        var toolbar = SrvUiKit.Row(
            SrvUiKit.Small("Add rule", "srv.filters.add", () => SrvUiKit.Run(() => EditAsync(null)), false),
            SrvUiKit.Small("View Sieve script", "srv.filters.sieve", () => SrvUiKit.Run(ShowSieveAsync)));
        ((Button)toolbar.Children[0]).IsEnabled = !_model.Locked;
        Body.Children.Add(toolbar);
        Body.Children.Add(SrvUiKit.Error(_model.SaveError));
        if (_model.Rows.Count == 0 && !_model.IsLoading)
        {
            Body.Children.Add(SrvUiKit.Dim("No rules yet. Add one to sort, flag or forward mail as it arrives."));
        }

        for (var i = 0; i < _model.Rows.Count; i++)
        {
            Body.Children.Add(RowOf(_model.Rows[i], i, _model.Rows.Count));
        }
    }

    private Border RowOf(SrvUiFilterRow row, int index, int count)
    {
        var id = row.Id;
        var enabled = new CheckBox { IsChecked = row.Enabled, IsEnabled = !_model.Locked, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        enabled.Click += (_, _) => SrvUiKit.Run(() => _model.SetEnabledAsync(id, enabled.IsChecked == true, CancellationToken.None));
        AutomationProperties.SetAutomationId(enabled, "srv.filters.enabled." + id);
        AutomationProperties.SetName(enabled, "Enable " + row.Name);
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(SrvUiKit.Strong(row.Name.Length == 0 ? "Rule" : row.Name));
        text.Children.Add(SrvUiKit.Dim(row.Summary));
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(enabled);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = SrvUiKit.Row(
            Enabled(SrvUiKit.Small("Up", "srv.filters.up." + id, () => SrvUiKit.Run(() => _model.MoveAsync(id, -1, CancellationToken.None))), index > 0 && !_model.Locked),
            Enabled(SrvUiKit.Small("Down", "srv.filters.down." + id, () => SrvUiKit.Run(() => _model.MoveAsync(id, 1, CancellationToken.None))), index < count - 1 && !_model.Locked),
            Enabled(SrvUiKit.Small("Edit", "srv.filters.edit." + id, () => SrvUiKit.Run(() => EditAsync(id))), !_model.Locked),
            SrvUiKit.Small("Apply to existing mail", "srv.filters.apply." + id, () => SrvUiKit.Run(() => ApplyAsync(id))),
            Enabled(SrvUiKit.Small("Delete", "srv.filters.delete." + id, () => SrvUiKit.Run(() => DeleteAsync(id, row.Name)), danger: true), !_model.Locked));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return SrvUiKit.Card(grid, "srv.filters.row." + id);
    }

    private static Button Enabled(Button button, bool on)
    {
        button.IsEnabled = on;
        return button;
    }

    private async Task EditAsync(string? ruleId)
    {
        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is null)
        {
            return;
        }

        var existing = ruleId is null ? null : _model.RuleOf(ruleId);
        var editor = new SrvUiFilterEditorModel(_model, existing);
        var saved = await dialogs.ShowAsync(new SrvUiFilterEditorRequest(editor), CancellationToken.None).ConfigureAwait(true);
        if (saved == true && editor.Saved is { } rule && ruleId is null)
        {
            await ApplyDialogAsync(rule).ConfigureAwait(true);
        }
    }

    private Task ApplyAsync(string id) => _model.RuleOf(id) is { } rule ? ApplyDialogAsync(rule) : Task.CompletedTask;

    private async Task ApplyDialogAsync(System.Text.Json.Nodes.JsonNode rule)
    {
        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is not null)
        {
            _ = await dialogs.ShowAsync(new SrvUiFilterApplyRequest(new SrvUiFilterApplyModel(_model, rule)), CancellationToken.None).ConfigureAwait(true);
        }
    }

    private async Task DeleteAsync(string id, string name)
    {
        var generation = _model.AccountGeneration;
        if (await SrvUiKit.AskAsync("Delete rule", "Delete the rule \"" + name + "\"? Mail it already sorted stays where it is.", "Delete").ConfigureAwait(true) && _model.IsCurrentGeneration(generation) && _model.RuleOf(id) is not null)
        {
            await _model.DeleteAsync(id, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private async Task ShowSieveAsync()
    {
        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is not null)
        {
            _ = await dialogs.ShowAsync(new SrvUiSieveRequest(_model.SieveText()), CancellationToken.None).ConfigureAwait(true);
        }
    }
}
